using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Filters;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;
using RestaurantMenu.Services;

namespace RestaurantMenu.Controllers;

/// <summary>
/// Stock for a branch: levels and their history (Editor and up record deliveries, usage,
/// waste and counts), ingredients and dish recipes (Manager and up). Table orders use stock
/// through the recipes (StockService.SyncOrderAsync).
/// </summary>
[Authorize(Roles = "OWNER,STAFF")]
[NoIndex]
[Route("branch/{id:int}/stock")]
[RequireModule(BillingModule.Management)]
public class StockController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IBranchAccess _access;
    private readonly StockService _stock;
    private readonly UserManager<ApplicationUser> _users;

    public StockController(ApplicationDbContext context, IBranchAccess access, StockService stock, UserManager<ApplicationUser> users)
    {
        _context = context;
        _access = access;
        _stock = stock;
        _users = users;
    }

    private Task<Branch?> BranchAsync(int id, BranchPermission p)
    {
        var allowed = _access.BranchIds(p);
        return _context.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id && allowed.Contains(b.Id));
    }

    private IActionResult Back(int id, string? anchor = null) => Redirect(Url.Action(nameof(Index), new { id }) + (anchor == null ? "" : "#" + anchor));

    [HttpGet("")]
    public async Task<IActionResult> Index(int id, bool archived = false)
    {
        var branch = await BranchAsync(id, BranchPermission.Stock);
        if (branch == null) return NotFound();
        var ingredients = await _context.Ingredients.AsNoTracking().Where(i => i.BranchId == id && i.IsArchived == archived)
            .OrderBy(i => i.Name).ToListAsync();
        var usedBy = await _context.DishIngredients.AsNoTracking().Where(d => d.Ingredient!.BranchId == id)
            .GroupBy(d => d.IngredientId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
        var recent = await _context.StockMovements.AsNoTracking().Include(m => m.Ingredient)
            .Where(m => m.BranchId == id).OrderByDescending(m => m.CreatedUtc).ThenByDescending(m => m.Id).Take(30).ToListAsync();
        ViewBag.CanSetup = await _access.CanAsync(id, BranchPermission.EditBranch);
        return View(new StockPage(branch, ingredients, usedBy, recent, archived,
            await _context.Ingredients.CountAsync(i => i.BranchId == id && i.IsArchived)));
    }

    [HttpGet("{ingredientId:int}")]
    public async Task<IActionResult> History(int id, int ingredientId)
    {
        var branch = await BranchAsync(id, BranchPermission.Stock);
        if (branch == null) return NotFound();
        var ingredient = await _context.Ingredients.AsNoTracking().FirstOrDefaultAsync(i => i.Id == ingredientId && i.BranchId == id);
        if (ingredient == null) return NotFound();
        ViewBag.Movements = await _context.StockMovements.AsNoTracking().Where(m => m.IngredientId == ingredientId)
            .OrderByDescending(m => m.CreatedUtc).ThenByDescending(m => m.Id).Take(200).ToListAsync();
        ViewBag.Dishes = await _context.DishIngredients.AsNoTracking().Where(d => d.IngredientId == ingredientId)
            .Select(d => new RecipeUse(d.Product!.Name, d.Quantity)).ToListAsync();
        ViewBag.CanSetup = await _access.CanAsync(id, BranchPermission.EditBranch);
        ViewBag.Branch = branch;
        return View(ingredient);
    }

    /// <summary>Delivery / used / waste (how much) or count (the level counted).</summary>
    [HttpPost("{ingredientId:int}/move")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Move(int id, int ingredientId, string? kind, string? amount, string? note, string? back)
    {
        if (await BranchAsync(id, BranchPermission.Stock) == null) return NotFound();
        // By name only: an unknown or numeric value must not fall back to a default kind.
        if (Kind(kind) is not { } k) return BadRequest();
        var value = StockRules.Parse(amount);
        if (value == null || (k != StockMovementKind.Count && value == 0))
        {
            TempData["Error"] = k == StockMovementKind.Count ? "Enter the amount you counted, like 12 or 2.5." : "Enter how much, like 12 or 2.5.";
            return BackTo(id, ingredientId, back);
        }
        var user = await _users.GetUserAsync(User);
        var m = await _stock.RecordAsync(id, ingredientId, k, value.Value, Clean(note, 200), user?.Id, user?.FullName ?? user?.Email, DateTime.UtcNow);
        if (m == null) return NotFound();
        var ing = await _context.Ingredients.AsNoTracking().FirstAsync(i => i.Id == ingredientId);
        TempData["Success"] = $"{ing.Name}: {StockRules.KindText(k).ToLowerInvariant()} saved. Now {StockRules.Format(m.QuantityAfter, ing.Unit)}.";
        return BackTo(id, ingredientId, back);
    }

    private static StockMovementKind? Kind(string? s) => s switch
    {
        "Delivery" => StockMovementKind.Delivery, "Usage" => StockMovementKind.Usage,
        "Waste" => StockMovementKind.Waste, "Count" => StockMovementKind.Count, _ => null
    };

    private static StockUnit? Unit(string? s) =>
        s != null && !s.Any(char.IsDigit) && Enum.TryParse<StockUnit>(s, false, out var u) && Enum.IsDefined(u) ? u : null;

    private IActionResult BackTo(int id, int ingredientId, string? back) =>
        back == "history" ? RedirectToAction(nameof(History), new { id, ingredientId }) : Back(id, $"i{ingredientId}");

    // ---------------------------------------------------------------- ingredients (Manager and up)

    [HttpPost("ingredients")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int id, string? name, string? unit, string? quantity, string? lowLevel, string? cost)
    {
        if (await BranchAsync(id, BranchPermission.EditBranch) == null) return NotFound();
        var (n, q, low, c, error) = Validate(name, Unit(unit), quantity ?? "0", lowLevel, cost);
        if (error == null && await _context.Ingredients.AnyAsync(i => i.BranchId == id && i.Name.ToLower() == n!.ToLower() && !i.IsArchived))
            error = $"There's already an ingredient called {n}.";
        if (error != null)
        {
            TempData["Error"] = error;
            return Back(id, "add");
        }
        var ing = new Ingredient { BranchId = id, Name = n!, Unit = Unit(unit)!.Value, Quantity = 0, LowLevel = low, CostPerUnit = c, CreatedUtc = DateTime.UtcNow };
        _context.Ingredients.Add(ing);
        await _context.SaveChangesAsync();
        if (q > 0)
        {
            var user = await _users.GetUserAsync(User);
            await _stock.RecordAsync(id, ing.Id, StockMovementKind.Count, q, "Opening stock", user?.Id, user?.FullName, DateTime.UtcNow);
        }
        TempData["Success"] = $"{ing.Name} added.";
        return Back(id, $"i{ing.Id}");
    }

    [HttpPost("{ingredientId:int}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, int ingredientId, string? name, string? unitName, string? lowLevel, string? cost)
    {
        if (await BranchAsync(id, BranchPermission.EditBranch) == null) return NotFound();
        var ing = await _context.Ingredients.FirstOrDefaultAsync(i => i.Id == ingredientId && i.BranchId == id);
        if (ing == null) return NotFound();
        var unit = Unit(unitName);
        var (n, _, low, c, error) = Validate(name, unit, "0", lowLevel, cost);
        if (error == null && await _context.Ingredients.AnyAsync(i => i.BranchId == id && i.Id != ingredientId && i.Name.ToLower() == n!.ToLower() && !i.IsArchived))
            error = $"There's already an ingredient called {n}.";
        if (error != null)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(History), new { id, ingredientId });
        }
        var unitChanged = ing.Unit != unit!.Value;
        ing.Name = n!;
        ing.Unit = unit.Value;
        ing.LowLevel = low;
        ing.CostPerUnit = c;
        await _context.SaveChangesAsync();
        TempData["Success"] = $"{ing.Name} saved." + (unitChanged ? " The unit changed: check the level and the recipes that use it, they're still in the old unit's numbers." : "");
        return RedirectToAction(nameof(History), new { id, ingredientId });
    }

    [HttpPost("{ingredientId:int}/archive")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Archive(int id, int ingredientId, bool archived)
    {
        if (await BranchAsync(id, BranchPermission.EditBranch) == null) return NotFound();
        var ing = await _context.Ingredients.FirstOrDefaultAsync(i => i.Id == ingredientId && i.BranchId == id);
        if (ing == null) return NotFound();
        ing.IsArchived = archived;
        if (archived)
        {
            // An archived ingredient no longer counts in recipes.
            _context.DishIngredients.RemoveRange(_context.DishIngredients.Where(d => d.IngredientId == ingredientId));
        }
        await _context.SaveChangesAsync();
        TempData["Success"] = archived ? $"{ing.Name} archived and taken out of recipes. Its history stays." : $"{ing.Name} is back in the list.";
        return Back(id);
    }

    private static (string? Name, decimal Quantity, decimal Low, decimal? Cost, string? Error) Validate(string? name, StockUnit? unit, string quantity, string? lowLevel, string? cost)
    {
        var n = Clean(name, 80);
        if (n == null) return (null, 0, 0, null, "Give the ingredient a name.");
        if (unit == null) return (null, 0, 0, null, "Choose a unit.");
        var q = StockRules.Parse(quantity);
        if (q == null) return (null, 0, 0, null, "The starting amount must be a number like 12 or 2.5.");
        var low = string.IsNullOrWhiteSpace(lowLevel) ? 0 : StockRules.Parse(lowLevel);
        if (low == null) return (null, 0, 0, null, "The low-stock level must be a number like 5.");
        decimal? c = null;
        if (!string.IsNullOrWhiteSpace(cost))
        {
            c = StockRules.Parse(cost);
            if (c == null || decimal.Round(c.Value, 2) != c) return (null, 0, 0, null, "The cost must be an amount like 4.50.");
        }
        return (n, q.Value, low.Value, c, null);
    }

    private static string? Clean(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = new string(s.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        return t.Length == 0 ? null : t.Length > max ? t[..max] : t;
    }

    // ---------------------------------------------------------------- recipes (Manager and up)

    [HttpGet("recipes")]
    public async Task<IActionResult> Recipes(int id)
    {
        var branch = await BranchAsync(id, BranchPermission.EditBranch);
        if (branch == null) return NotFound();
        var dishes = await _context.Products.AsNoTracking().Where(p => p.BranchId == id)
            .OrderBy(p => p.Category!.Priority).ThenBy(p => p.DisplayOrder).ThenBy(p => p.Id)
            .Select(p => new { p.Id, p.Name, Category = p.Category!.Name }).ToListAsync();
        var lines = await _context.DishIngredients.AsNoTracking().Where(d => d.Product!.BranchId == id).ToListAsync();
        var ingredients = await _context.Ingredients.AsNoTracking().Where(i => i.BranchId == id && !i.IsArchived).OrderBy(i => i.Name).ToListAsync();
        return View(new RecipesPage(branch,
            dishes.Select(d => new RecipeDish(d.Id, d.Name, d.Category, lines.Where(l => l.ProductId == d.Id).ToList())).ToList(),
            ingredients));
    }

    /// <summary>Saves one dish's recipe: parallel lists ingredient[] and qty[]; empty rows are ignored.</summary>
    [HttpPost("recipes/{productId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveRecipe(int id, int productId, List<int>? ingredient, List<string>? qty)
    {
        if (await BranchAsync(id, BranchPermission.EditBranch) == null) return NotFound();
        var dish = await _context.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId && p.BranchId == id);
        if (dish == null) return NotFound();
        var valid = await _context.Ingredients.AsNoTracking().Where(i => i.BranchId == id && !i.IsArchived).Select(i => i.Id).ToListAsync();

        var rows = new Dictionary<int, decimal>();
        ingredient ??= new List<int>();
        qty ??= new List<string>();
        for (var i = 0; i < ingredient.Count; i++)
        {
            var ingId = ingredient[i];
            var raw = i < qty.Count ? qty[i] : null;
            if (ingId == 0 && string.IsNullOrWhiteSpace(raw)) continue;
            var q = StockRules.Parse(raw);
            if (!valid.Contains(ingId) || q is null or 0)
            {
                TempData["Error"] = $"{dish.Name}: each line needs an ingredient and an amount per portion, like 0.25.";
                return Redirect(Url.Action(nameof(Recipes), new { id }) + $"#d{productId}");
            }
            if (rows.ContainsKey(ingId))
            {
                TempData["Error"] = $"{dish.Name}: an ingredient is listed twice.";
                return Redirect(Url.Action(nameof(Recipes), new { id }) + $"#d{productId}");
            }
            rows[ingId] = q.Value;
        }

        _context.DishIngredients.RemoveRange(_context.DishIngredients.Where(d => d.ProductId == productId));
        _context.DishIngredients.AddRange(rows.Select(r => new DishIngredient { ProductId = productId, IngredientId = r.Key, Quantity = r.Value }));
        await _context.SaveChangesAsync();
        TempData["Success"] = rows.Count == 0 ? $"{dish.Name} no longer uses stock." : $"{dish.Name}: recipe saved ({rows.Count} ingredient{(rows.Count == 1 ? "" : "s")}). Table orders now use it.";
        return Redirect(Url.Action(nameof(Recipes), new { id }) + $"#d{productId}");
    }
}

public record StockPage(Branch Branch, IReadOnlyList<Ingredient> Ingredients, IReadOnlyDictionary<int, int> UsedBy,
    IReadOnlyList<StockMovement> Recent, bool Archived, int ArchivedCount);
public record RecipeUse(string Dish, decimal Quantity);
public record RecipeDish(int Id, string Name, string Category, IReadOnlyList<DishIngredient> Lines);
public record RecipesPage(Branch Branch, IReadOnlyList<RecipeDish> Dishes, IReadOnlyList<Ingredient> Ingredients);
