namespace RestaurantMenu.Helpers;

/// <summary>
/// Guest-facing words for table QR codes, shared by the menu ("Table 14") and the
/// printed tents and stickers, so both always say the same thing in each language.
/// </summary>
public static class TableText
{
    private static readonly Dictionary<string, (string Table, string Scan)> Texts = new()
    {
        ["en"] = ("Table", "Scan for the menu"),
        ["sq"] = ("Tavolina", "Skanoni për menunë"),
        ["it"] = ("Tavolo", "Inquadra per il menu"),
        ["de"] = ("Tisch", "Scannen für die Speisekarte"),
        ["fr"] = ("Table", "Scannez pour le menu"),
        ["es"] = ("Mesa", "Escanea para ver el menú"),
        ["tr"] = ("Masa", "Menü için okutun")
    };

    private static (string Table, string Scan) For(string? language) =>
        Texts.TryGetValue(language ?? "en", out var text) ? text : Texts["en"];

    /// <summary>"Table 14", "Tavolina 14", …</summary>
    public static string Label(int table, string? language) => $"{For(language).Table} {table}";

    /// <summary>The call to action printed above the QR code.</summary>
    public static string ScanPrompt(string? language) => For(language).Scan;
}
