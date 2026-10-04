namespace RestaurantMenu.Models;

/// <summary>Persisted as int; never renumber.</summary>
public enum StockUnit
{
    Piece = 0,
    Kilogram = 1,
    Gram = 2,
    Litre = 3,
    Millilitre = 4,
    Bottle = 5,
    Box = 6
}

/// <summary>
/// Something the kitchen keeps in stock. <see cref="Quantity"/> is the current level and
/// only changes through a <see cref="StockMovement"/>, so every change has a reason and an
/// author. Archived ingredients keep their history but leave the lists.
/// </summary>
public class Ingredient
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }

    public string Name { get; set; } = "";
    public StockUnit Unit { get; set; }
    public decimal Quantity { get; set; }
    /// <summary>At or below this the ingredient shows as low (0 = no warning).</summary>
    public decimal LowLevel { get; set; }
    /// <summary>Optional, per unit, for the stock value.</summary>
    public decimal? CostPerUnit { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedUtc { get; set; }
}

/// <summary>Persisted as int; never renumber.</summary>
public enum StockMovementKind
{
    /// <summary>Goods in.</summary>
    Delivery = 0,
    /// <summary>Taken out by hand (prep, staff meal).</summary>
    Usage = 1,
    Waste = 2,
    /// <summary>A stock count: the level was set to what was counted.</summary>
    Count = 3,
    /// <summary>Used by a table order (recipes), automatic.</summary>
    Sale = 4,
    /// <summary>A cancelled order gave its ingredients back, automatic.</summary>
    SaleReturn = 5
}

public class StockMovement
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public int IngredientId { get; set; }
    public Ingredient? Ingredient { get; set; }

    public StockMovementKind Kind { get; set; }
    /// <summary>Signed change: + in, − out.</summary>
    public decimal Change { get; set; }
    /// <summary>The level right after this movement.</summary>
    public decimal QuantityAfter { get; set; }
    public string? Note { get; set; }
    public int? OrderId { get; set; }
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public DateTime CreatedUtc { get; set; }
}

/// <summary>How much of an ingredient one portion of a dish uses (its recipe line).</summary>
public class DishIngredient
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public int IngredientId { get; set; }
    public Ingredient? Ingredient { get; set; }
    public decimal Quantity { get; set; }
}

/// <summary>
/// One person working one stretch at a branch. A team member (UserId) or anyone else by
/// name, since not every waiter has an account. Times are wall clock in the branch.
/// </summary>
public class Shift
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }

    public string? UserId { get; set; }
    public string PersonName { get; set; } = "";
    /// <summary>Where they work: Kitchen, Bar, Floor… free text.</summary>
    public string? Station { get; set; }
    public DateTime StartsLocal { get; set; }
    public DateTime EndsLocal { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedUtc { get; set; }
}

/// <summary>
/// One branch's business day: the table orders rolled up (nightly, and on demand), plus
/// what the manager counted at closing (cash, card), so the two can be compared.
/// </summary>
public class DailySales
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }
    public DateOnly Date { get; set; }

    // Rolled up from orders (not cancelled).
    public int Orders { get; set; }
    public int Items { get; set; }
    public decimal Revenue { get; set; }
    public int CancelledOrders { get; set; }
    /// <summary>Top dishes that day, JSON [{name, qty, revenue}].</summary>
    public string? TopDishes { get; set; }
    public DateTime ComputedUtc { get; set; }

    // End of day, entered by a manager.
    public decimal? CashTotal { get; set; }
    public decimal? CardTotal { get; set; }
    public string? CloseNote { get; set; }
    public string? ClosedByName { get; set; }
    public DateTime? ClosedUtc { get; set; }
}
