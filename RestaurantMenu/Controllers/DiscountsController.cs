using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Filters;
using RestaurantMenu.Models;
using RestaurantMenu.Services;

namespace RestaurantMenu.Controllers;

[Authorize(Roles = "OWNER,STAFF")]
[NoIndex]
[RequireModule(BillingModule.Ordering)]
[Route("branch/{id:int}/discounts")]
public class DiscountsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IBranchAccess _access;
    public DiscountsController(ApplicationDbContext db, IBranchAccess access) { _db = db; _access = access; }

    [HttpGet("")]
    public async Task<IActionResult> Index(int id)
    {
        var branch = await _access.Branches(BranchPermission.EditBranch).AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);
        if (branch == null) return NotFound();
        var discounts = await _db.Discounts.AsNoTracking().Include(d => d.Product)
            .Where(d => d.BranchId == id).OrderByDescending(d => d.Enabled).ThenBy(d => d.Name).ToListAsync();
        var products = await _db.Products.AsNoTracking().Where(p => p.BranchId == id)
            .OrderBy(p => p.Name).Select(p => new ProductChoice(p.Id, p.Name)).ToListAsync();
        return View(new DiscountsPage(branch, discounts, products));
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int id, string? name, DiscountKind kind, DiscountScope scope,
        decimal value, int? productId, DayOfWeek? dayOfWeek, TimeOnly? startsAt, TimeOnly? endsAt, bool guestMenu)
    {
        if (!await _access.CanAsync(id, BranchPermission.EditBranch)) return NotFound();
        name = name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || value <= 0 ||
            (kind == DiscountKind.Percentage && value > 100) || !Enum.IsDefined(kind) || !Enum.IsDefined(scope))
        {
            TempData["Error"] = "Enter a name and a valid positive discount (percentage cannot exceed 100).";
            return RedirectToAction(nameof(Index), new { id });
        }
        if (productId != null && !await _db.Products.AnyAsync(p => p.Id == productId && p.BranchId == id)) return BadRequest();
        _db.Discounts.Add(new Discount { BranchId = id, Name = name, Kind = kind, Scope = scope, Value = value,
            ProductId = scope == DiscountScope.Item ? productId : null, DayOfWeek = dayOfWeek, StartsAt = startsAt,
            EndsAt = endsAt, PosOnly = !guestMenu, Enabled = true, CreatedUtc = DateTime.UtcNow });
        await _db.SaveChangesAsync();
        TempData["Success"] = $"{name} added.";
        return RedirectToAction(nameof(Index), new { id });
    }

    [HttpPost("{discountId:int}/toggle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id, int discountId)
    {
        if (!await _access.CanAsync(id, BranchPermission.EditBranch)) return NotFound();
        var discount = await _db.Discounts.FirstOrDefaultAsync(d => d.Id == discountId && d.BranchId == id);
        if (discount == null) return NotFound();
        discount.Enabled = !discount.Enabled;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { id });
    }
}

public record ProductChoice(int Id, string Name);
public record DiscountsPage(Branch Branch, IReadOnlyList<Discount> Discounts, IReadOnlyList<ProductChoice> Products);
