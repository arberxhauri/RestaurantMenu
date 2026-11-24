using System.Text.RegularExpressions;
using RestaurantMenu.Models;

namespace RestaurantMenu.Helpers;

/// <summary>One line of a bill: which dish, its unit price (options included) and how many.</summary>
public sealed record BillLine(int? ProductId, decimal UnitPrice, int Quantity);

/// <summary>A bill's money: lines added up, the discount taken off, what is left to pay.</summary>
public sealed record BillSummary(decimal Subtotal, decimal Discount, decimal Total);

/// <summary>One way a guest pays part of a bill.</summary>
public sealed record Tender(PaymentMethod Method, decimal Amount);

/// <summary>Whether the tenders pay the bill, the cash to give back, and why not.</summary>
public sealed record PaymentCheck(bool Ok, decimal Change, string? Problem);

/// <summary>What the POS sends to place an order (docs/PLAN-backoffice.md, phase 6).</summary>
public sealed class PosOrderRequest
{
    public Guid RequestId { get; set; }
    public string? Type { get; set; }
    public int? TableId { get; set; }
    public int Guests { get; set; }
    public string? Note { get; set; }
    public List<OrderLineRequest>? Items { get; set; }
}

/// <summary>
/// The POS's money rules in one place (tested in PosRulesTests): when a discount is on and how much
/// it takes off, the bill's totals, and whether a payment covers it. The POS records payments for the
/// restaurant's own reports and prints a pro-forma bill; it is not a fiscal receipt (plan §8).
/// </summary>
public static partial class PosRules
{
    public const int MaxTenders = 4;

    /// <summary>Whether a discount applies at this local time (its weekday and happy-hour window, which may run past midnight).</summary>
    public static bool DiscountActive(Discount discount, DateTime localNow)
    {
        if (!discount.Enabled) return false;
        var time = TimeOnly.FromDateTime(localNow);
        var day = localNow.DayOfWeek;

        switch (discount.StartsAt, discount.EndsAt)
        {
            case ({ } start, { } end) when start < end:
                if (time < start || time >= end) return false;
                break;
            case ({ } start, { } end) when start > end:
                // Over midnight: before the end it still counts as the evening it started.
                if (time < end) day = day == DayOfWeek.Sunday ? DayOfWeek.Saturday : day - 1;
                else if (time < start) return false;
                break;
            case ({ } start, null):
                if (time < start) return false;
                break;
            case (null, { } end):
                if (time >= end) return false;
                break;
        }
        return discount.DayOfWeek == null || discount.DayOfWeek == day;
    }

    /// <summary>How much a discount takes off these lines (never more than what it applies to).</summary>
    public static decimal DiscountAmount(Discount discount, IReadOnlyList<BillLine> lines)
    {
        if (discount.Value <= 0) return 0m;
        if (discount.Kind == DiscountKind.Percentage && discount.Value > 100) return 0m;

        var basis = discount.Scope == DiscountScope.Item
            ? lines.Where(l => discount.ProductId != null && l.ProductId == discount.ProductId).Sum(LineTotal)
            : lines.Sum(LineTotal);
        if (basis <= 0) return 0m;

        var amount = discount.Kind == DiscountKind.Percentage
            ? Math.Round(basis * discount.Value / 100m, 2, MidpointRounding.AwayFromZero)
            : discount.Value;
        return Math.Round(Math.Min(amount, basis), 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>The bill's totals; a discount can take it down to zero, never below.</summary>
    public static BillSummary Summarize(IEnumerable<BillLine> lines, decimal discount)
    {
        var subtotal = Math.Round(lines.Sum(LineTotal), 2, MidpointRounding.AwayFromZero);
        var off = Math.Clamp(Math.Round(discount, 2, MidpointRounding.AwayFromZero), 0m, subtotal);
        return new BillSummary(subtotal, off, subtotal - off);
    }

    /// <summary>
    /// Whether the tenders pay <paramref name="due"/>. Cards (and other methods) are charged at most the
    /// bill; anything above it must be cash and is given back as change.
    /// </summary>
    public static PaymentCheck CheckPayment(decimal due, IReadOnlyList<Tender> tenders)
    {
        due = Math.Max(0m, due);
        if (tenders.Count > MaxTenders) return new(false, 0m, $"Use at most {MaxTenders} payments for one bill.");
        if (tenders.Any(t => t.Amount <= 0 || !Enum.IsDefined(t.Method))) return new(false, 0m, "Enter amounts above zero.");

        var total = tenders.Sum(t => t.Amount);
        if (total < due) return new(false, 0m, "That doesn't cover the bill.");
        var notCash = tenders.Where(t => t.Method != PaymentMethod.Cash).Sum(t => t.Amount);
        if (notCash > due) return new(false, 0m, "A card can't be charged more than the bill; take the rest in cash.");
        return new(true, Math.Round(total - due, 2, MidpointRounding.AwayFromZero), null);
    }

    /// <summary>"t12" is table 12's bill (all its open orders); "o345" is order 345 on its own (takeaway).</summary>
    public static string BillKey(int? tableId, int? orderId) => tableId is { } t ? $"t{t}" : $"o{orderId}";

    public static (int? TableId, int? OrderId) ParseBillKey(string? key)
    {
        var m = key == null ? null : BillKeyPattern().Match(key);
        if (m is not { Success: true } || !int.TryParse(m.Groups[2].Value, out var id)) return (null, null);
        return m.Groups[1].Value == "t" ? (id, null) : (null, id);
    }

    public static OrderType? ParseOrderType(string? value) => value?.ToLowerInvariant() switch
    {
        "dinein" => OrderType.DineIn,
        "takeaway" => OrderType.Takeaway,
        _ => null
    };

    private static decimal LineTotal(BillLine l) => l.UnitPrice * l.Quantity;

    [GeneratedRegex("^([to])([1-9][0-9]{0,8})$")]
    private static partial Regex BillKeyPattern();
}
