using System.Security.Cryptography;
using RestaurantMenu.Models;

namespace RestaurantMenu.Helpers;

/// <summary>What a guest's phone sends: dish ids, quantities and chosen options. Never prices.</summary>
public class OrderRequest
{
    public int Branch { get; set; }
    public int Table { get; set; }
    public string? Code { get; set; }
    public Guid RequestId { get; set; }
    public string? Lang { get; set; }
    public string? Note { get; set; }
    public List<OrderLineRequest>? Items { get; set; }
}

public class OrderLineRequest
{
    public int Id { get; set; }
    public int Qty { get; set; }
    public List<int>? Options { get; set; }
}

/// <summary>Why an order was refused; each has a guest-facing message in <see cref="OrderText"/>.</summary>
public enum OrderProblem
{
    /// <summary>Ordering is off, paused, or the table/code isn't (or no longer) valid.</summary>
    Unavailable,
    /// <summary>Outside the branch's opening hours.</summary>
    Closed,
    /// <summary>Some dishes are sold out or not served at this time (ids returned).</summary>
    NotAvailableNow,
    /// <summary>A dish or its choices changed since the menu was loaded.</summary>
    MenuChanged,
    Empty,
    TooBig,
    /// <summary>Too many orders from this table in a short time.</summary>
    Busy,
    /// <summary>Malformed request (not something a real menu page sends).</summary>
    Invalid
}

public record PricedLine(int ProductId, string Name, string? Options, string? OptionIds, decimal UnitPrice, int Quantity);

/// <summary>
/// The rules for a table order, with no database: limits, dish and option checks, and
/// prices taken only from the menu. Unit-tested; OrderService does the rest.
/// </summary>
public static class OrderRules
{
    public const int MaxLines = 40;
    public const int MaxQuantityPerLine = 20;
    public const int MaxItems = 100;
    public const int MaxNote = 200;

    public static string? CleanNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note)) return null;
        // One line of plain text: control characters (newlines included) become spaces.
        var clean = new string(note.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        while (clean.Contains("  ")) clean = clean.Replace("  ", " ");
        return clean.Length == 0 ? null : clean.Length > MaxNote ? clean[..MaxNote] : clean;
    }

    /// <summary>
    /// Prices the lines from <paramref name="products"/> (this branch's dishes with their
    /// option groups). <paramref name="servedNow"/> says whether a dish's category is served
    /// at this moment. Identical lines are merged. Returns the problem and, for
    /// <see cref="OrderProblem.NotAvailableNow"/>, the dish ids concerned.
    /// </summary>
    public static (List<PricedLine> Lines, OrderProblem? Problem, List<int> UnavailableIds) Price(
        IReadOnlyList<OrderLineRequest>? request, IReadOnlyDictionary<int, Product> products, Func<Product, bool> servedNow)
    {
        var none = new List<int>();
        if (request == null || request.Count == 0) return (new(), OrderProblem.Empty, none);
        if (request.Count > MaxLines) return (new(), OrderProblem.TooBig, none);
        if (request.Any(l => l.Qty < 1 || l.Qty > MaxQuantityPerLine)) return (new(), OrderProblem.Invalid, none);
        if (request.Sum(l => l.Qty) > MaxItems) return (new(), OrderProblem.TooBig, none);

        var unavailable = new List<int>();
        var lines = new List<PricedLine>();
        foreach (var req in request)
        {
            if (!products.TryGetValue(req.Id, out var p)) return (new(), OrderProblem.MenuChanged, none);
            if (!p.IsAvailable || !servedNow(p))
            {
                if (!unavailable.Contains(p.Id)) unavailable.Add(p.Id);
                continue;
            }

            var chosen = (req.Options ?? new List<int>()).ToList();
            if (chosen.Count != chosen.Distinct().Count()) return (new(), OrderProblem.Invalid, none);
            var groups = (p.OptionGroups ?? new List<ProductOptionGroup>())
                .Where(g => g.Options.Any())
                .OrderBy(g => g.DisplayOrder).ThenBy(g => g.Id).ToList();
            var all = groups.SelectMany(g => g.Options).ToDictionary(o => o.Id);
            if (chosen.Any(id => !all.ContainsKey(id))) return (new(), OrderProblem.MenuChanged, none);

            var picked = new List<ProductOption>();
            foreach (var g in groups)
            {
                var inGroup = g.Options.Where(o => chosen.Contains(o.Id)).OrderBy(o => o.DisplayOrder).ThenBy(o => o.Id).ToList();
                if (inGroup.Count < g.MinSelect || inGroup.Count > g.MaxSelect) return (new(), OrderProblem.MenuChanged, none);
                picked.AddRange(inGroup);
            }

            var unit = Math.Max(0, p.Price + picked.Sum(o => o.PriceDelta));
            var optionText = picked.Count == 0 ? null : string.Join(", ", picked.Select(o => o.Name));
            var optionIds = picked.Count == 0 ? null : string.Join(",", picked.Select(o => o.Id).OrderBy(i => i));
            var same = lines.FindIndex(l => l.ProductId == p.Id && l.OptionIds == optionIds);
            if (same >= 0)
            {
                var merged = lines[same];
                lines[same] = merged with { Quantity = merged.Quantity + req.Qty };
            }
            else
            {
                lines.Add(new PricedLine(p.Id, p.Name, optionText, optionIds, unit, req.Qty));
            }
        }

        if (unavailable.Count > 0) return (new(), OrderProblem.NotAvailableNow, unavailable);
        return (lines, null, none);
    }

    // ---------------------------------------------------------------- table codes

    // No 0/O, 1/I/L: read aloud or typed, they can't be confused.
    private const string CodeAlphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
    public const int CodeLength = 6;

    /// <summary>A random table code (31^6, about 900 million).</summary>
    public static string NewCode() =>
        string.Create(CodeLength, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++) span[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        });

    /// <summary>Constant-time, case-insensitive comparison of a code from a link.</summary>
    public static bool CodeMatches(string expected, string? given)
    {
        if (string.IsNullOrEmpty(given) || string.IsNullOrEmpty(expected)) return false;
        var a = System.Text.Encoding.ASCII.GetBytes(expected.ToUpperInvariant());
        var b = System.Text.Encoding.ASCII.GetBytes(given.Trim().ToUpperInvariant());
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
