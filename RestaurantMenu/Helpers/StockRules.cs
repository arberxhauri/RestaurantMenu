using System.Globalization;
using RestaurantMenu.Models;

namespace RestaurantMenu.Helpers;

/// <summary>Units, quantities and the words for stock (back office, English).</summary>
public static class StockRules
{
    public const decimal MaxQuantity = 1_000_000m;

    public static string Short(StockUnit u) => u switch
    {
        StockUnit.Kilogram => "kg", StockUnit.Gram => "g", StockUnit.Litre => "l", StockUnit.Millilitre => "ml",
        StockUnit.Bottle => "bottles", StockUnit.Box => "boxes", _ => "pcs"
    };

    public static string Long(StockUnit u) => u switch
    {
        StockUnit.Kilogram => "Kilograms (kg)", StockUnit.Gram => "Grams (g)", StockUnit.Litre => "Litres (l)",
        StockUnit.Millilitre => "Millilitres (ml)", StockUnit.Bottle => "Bottles", StockUnit.Box => "Boxes", _ => "Pieces"
    };

    /// <summary>"1.5", "1,5", " 2 " → the number; null if it isn't one (or has more than 3 decimals, or is too big).</summary>
    public static decimal? Parse(string? input, bool allowNegative = false)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var s = input.Trim().Replace(" ", "");
        // One comma and no dot: a decimal comma ("1,5"). Otherwise commas are thousands separators.
        s = s.Contains(',') && !s.Contains('.') && s.Count(c => c == ',') == 1 ? s.Replace(',', '.') : s.Replace(",", "");
        if (!decimal.TryParse(s, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v)) return null;
        if (decimal.Round(v, 3) != v || Math.Abs(v) > MaxQuantity || (!allowNegative && v < 0)) return null;
        return v;
    }

    /// <summary>1.500 → "1.5", 2.000 → "2", with the unit: "1.5 kg".</summary>
    public static string Format(decimal q, StockUnit? unit = null)
    {
        var n = q.ToString("#,0.###", CultureInfo.GetCultureInfo("en-GB"));
        return unit == null ? n : $"{n} {Short(unit.Value)}";
    }

    /// <summary>Low: at or below the warning level (when one is set), or nothing left.</summary>
    public static bool IsLow(Ingredient i) => i.Quantity <= 0 || (i.LowLevel > 0 && i.Quantity <= i.LowLevel);

    public static string KindText(StockMovementKind k) => k switch
    {
        StockMovementKind.Delivery => "Delivery", StockMovementKind.Usage => "Used", StockMovementKind.Waste => "Waste",
        StockMovementKind.Count => "Stock count", StockMovementKind.Sale => "Orders", _ => "Order cancelled"
    };
}
