namespace RestaurantMenu.Models;

/// <summary>
/// A table guests can order from. Its QR code carries the number and <see cref="Code"/>
/// (?t=12&amp;k=7QX4MP): the code is what lets a phone send orders, so typing ?t=12 at
/// home isn't enough. New codes (Tables page) stop old printouts and links from ordering.
/// Hard-deleted: orders keep the table number they were placed at.
/// </summary>
public class DiningTable
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }

    /// <summary>1 to 9999, unique per branch; what the QR code and the kitchen show.</summary>
    public int Number { get; set; }
    /// <summary>Optional place, e.g. "Terrace", shown next to the number in the kitchen.</summary>
    public string? Name { get; set; }
    public string Code { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
}

/// <summary>Persisted as int; never renumber.</summary>
public enum OrderStatus
{
    New = 0,
    Preparing = 1,
    Served = 2,
    Cancelled = 3
}

/// <summary>
/// An order sent from a table. Everything the kitchen needs is copied in when it is
/// placed (table, dish names, options, prices), so later menu edits never change it.
/// </summary>
public class Order
{
    public int Id { get; set; }
    /// <summary>The guest's handle for checking the status; unguessable, never shown.</summary>
    public Guid PublicId { get; set; }
    /// <summary>Sent by the guest's phone with each attempt, so a retry never orders twice.</summary>
    public Guid ClientRequestId { get; set; }

    public int BranchId { get; set; }
    public Branch? Branch { get; set; }
    public int? TableId { get; set; }
    public int TableNumber { get; set; }
    public string? TableName { get; set; }

    /// <summary>Restarts every day (branch time): "#12".</summary>
    public int Number { get; set; }
    public DateOnly OrderDay { get; set; }

    public OrderStatus Status { get; set; }
    /// <summary>Guest's note for the kitchen, at most 200 characters.</summary>
    public string? Note { get; set; }
    /// <summary>The menu language the guest ordered in.</summary>
    public string Language { get; set; } = "en";
    public decimal Total { get; set; }

    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }

    public List<OrderItem> Items { get; set; } = new();
}

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order? Order { get; set; }

    /// <summary>No foreign key: the order outlives the dish.</summary>
    public int? ProductId { get; set; }
    /// <summary>The dish name in the restaurant's own language, as the kitchen knows it.</summary>
    public string Name { get; set; } = "";
    /// <summary>"Large, Extra cheese", in the restaurant's language; null without choices.</summary>
    public string? Options { get; set; }
    /// <summary>Chosen option ids, comma separated, for reports later.</summary>
    public string? OptionIds { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}
