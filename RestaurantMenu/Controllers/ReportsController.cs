using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Filters;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;
using RestaurantMenu.Services;

namespace RestaurantMenu.Controllers;

/// <summary>The branch's reports with date range, comparison and CSV export (managers and up).</summary>
[Authorize(Roles = "OWNER,STAFF")]
[NoIndex]
[Route("branch/{id:int}/reports")]
[RequireModule(BillingModule.Management)]
public class ReportsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IBranchAccess _access;
    private readonly ReportService _reports;

    public ReportsController(ApplicationDbContext db, IBranchAccess access, ReportService reports)
    {
        _db = db;
        _access = access;
        _reports = reports;
    }

    private async Task<(Branch? Branch, ReportPage? Page)> LoadAsync(int id, string? from, string? to)
    {
        var allowed = _access.BranchIds(BranchPermission.ViewInsights);
        var branch = await _db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id && allowed.Contains(b.Id));
        if (branch == null) return (null, null);
        var zone = OpeningHours.Zone(branch.TimeZone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
        var (f, t) = ReportRules.Period(from, to, today);
        return (branch, await _reports.BuildAsync(id, f, t, zone));
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(int id, string? from, string? to)
    {
        var (branch, page) = await LoadAsync(id, from, to);
        if (branch == null || page == null) return NotFound();
        ViewBag.Branch = branch;
        ViewBag.Currency = CurrencyHelper.GetCurrencySymbol(branch.Currency);
        return View(page);
    }

    [HttpGet("export.csv")]
    public async Task<IActionResult> Export(int id, string? from, string? to)
    {
        var (branch, page) = await LoadAsync(id, from, to);
        if (branch == null || page == null) return NotFound();
        string M(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);

        var rows = new List<string[]>
        {
            new[] { "Report", branch.Name, page.From.ToString("yyyy-MM-dd"), page.To.ToString("yyyy-MM-dd") },
            new[] { "Sales", M(page.Now.Revenue) }, new[] { "Orders", page.Now.Orders.ToString(CultureInfo.InvariantCulture) },
            new[] { "Average bill", M(page.Now.AverageBill) }, new[] { "Discounts", M(page.Now.Discounts) },
            new[] { "Waste cost", M(page.Now.Waste) }, Array.Empty<string>(),
            new[] { "Dish", "Quantity", "Revenue" }
        };
        rows.AddRange(page.Dishes.Select(d => new[] { d.Name, d.Quantity.ToString(CultureInfo.InvariantCulture), M(d.Revenue) }));
        rows.Add(Array.Empty<string>());
        rows.Add(new[] { "Category", "Quantity", "Revenue" });
        rows.AddRange(page.Categories.Select(c => new[] { c.Name, c.Quantity.ToString(CultureInfo.InvariantCulture), M(c.Revenue) }));

        var name = $"report-{page.From:yyyy-MM-dd}-{page.To:yyyy-MM-dd}.csv";
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(ReportRules.Csv(rows))).ToArray(), "text/csv", name);
    }
}
