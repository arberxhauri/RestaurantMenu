using System.Globalization;

namespace RestaurantMenu.Helpers;

/// <summary>Prices typed in the admin: "15", "15.5", "15,50", "1 500" → cents (tested in MoneyInputTests).</summary>
public static class MoneyInput
{
    public const int MaxCents = 100_000_000; // 1,000,000.00

    /// <summary>
    /// True with the amount in cents, or with null for an empty box ("no price"). False when it
    /// isn't an amount: letters, a negative number, more than two decimals, or too large.
    /// </summary>
    public static bool TryParseCents(string? text, out int? cents)
    {
        cents = null;
        var t = (text ?? "").Trim().Replace(" ", "").Replace(" ", "").Replace("€", "");
        if (t.Length == 0) return true;
        // One separator, a comma or a dot, is the decimal point when one or two digits follow it.
        var sep = t.LastIndexOfAny(new[] { ',', '.' });
        string whole = t, fraction = "";
        if (sep >= 0)
        {
            fraction = t[(sep + 1)..];
            whole = t[..sep];
            if (fraction.Length is 0 or > 2 || whole.Contains(',') || whole.Contains('.')) return false;
        }
        if (whole.Length == 0 || !whole.All(char.IsAsciiDigit) || !fraction.All(char.IsAsciiDigit)) return false;
        if (!long.TryParse(whole, NumberStyles.None, CultureInfo.InvariantCulture, out var units)) return false;
        var value = units * 100 + (fraction.Length == 0 ? 0 : int.Parse(fraction.PadRight(2, '0'), CultureInfo.InvariantCulture));
        if (value > MaxCents) return false;
        cents = (int)value;
        return true;
    }

    /// <summary>Cents back into the admin's box: 1500 → "15.00".</summary>
    public static string Format(int? cents) =>
        cents is { } c ? (c / 100m).ToString("0.00", CultureInfo.InvariantCulture) : "";
}
