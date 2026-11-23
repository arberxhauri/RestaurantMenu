namespace RestaurantMenu.Models;

[Flags]
public enum ServiceStation { None = 0, Kitchen = 1, Bar = 2 }
public enum TableShape { Square = 0, Round = 1, Rectangle = 2 }
public enum OrderSource { GuestQr = 0, Pos = 1 }
public enum OrderType { DineIn = 0, Takeaway = 1 }
public enum TicketStatus { New = 0, Preparing = 1, Ready = 2, Served = 3, Cancelled = 4 }
public enum PaymentMethod { Cash = 0, Card = 1, Other = 2 }
public enum DiscountKind { Percentage = 0, FixedAmount = 1 }
public enum DiscountScope { Bill = 0, Item = 1 }
public enum WasteReason { Expired = 0, Damaged = 1, PrepLoss = 2, Returned = 3, Other = 4 }

public class TableArea
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }
    public string Name { get; set; } = "";
    public int DisplayOrder { get; set; }
}

public class OrderTicket
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order? Order { get; set; }
    public ServiceStation Station { get; set; }
    public TicketStatus Status { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? ReadyUtc { get; set; }
    public DateTime? ServedUtc { get; set; }
}

public class Payment
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order? Order { get; set; }
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public decimal Tip { get; set; }
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public DateTime CreatedUtc { get; set; }
}

public class Discount
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }
    public string Name { get; set; } = "";
    public DiscountKind Kind { get; set; }
    public DiscountScope Scope { get; set; }
    public decimal Value { get; set; }
    public int? ProductId { get; set; }
    public Product? Product { get; set; }
    public DayOfWeek? DayOfWeek { get; set; }
    public TimeOnly? StartsAt { get; set; }
    public TimeOnly? EndsAt { get; set; }
    public bool PosOnly { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public DateTime CreatedUtc { get; set; }
}

public class OrderDiscount
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order? Order { get; set; }
    public int? DiscountId { get; set; }
    public Discount? Discount { get; set; }
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
    public string? AppliedById { get; set; }
    public DateTime CreatedUtc { get; set; }
}

public class StockCategory
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }
    public string Name { get; set; } = "";
    public int DisplayOrder { get; set; }
}

public class StationDevice
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }
    public string Name { get; set; } = "";
    public ServiceStation Station { get; set; }
    public string TokenHash { get; set; } = "";
    public bool Revoked { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? LastSeenUtc { get; set; }
}
