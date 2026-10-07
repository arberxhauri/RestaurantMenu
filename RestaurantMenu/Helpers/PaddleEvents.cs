using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RestaurantMenu.Helpers;

/// <summary>
/// Paddle's webhook signature (tested in PaddleEventsTests). The Paddle-Signature header is
/// "ts=1671552777;h1=…": h1 is the hex HMAC-SHA256 of "{ts}:{raw body}" with the endpoint's secret.
/// There can be several h1 values while a secret is being rotated. Old timestamps are refused so
/// a captured request can't be replayed later.
/// </summary>
public static class PaddleSignature
{
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    public static bool IsValid(string? header, string rawBody, string? secret, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(header) || string.IsNullOrEmpty(secret)) return false;
        string? ts = null;
        var signatures = new List<string>();
        foreach (var part in header.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var (key, value) = (part[..eq], part[(eq + 1)..]);
            if (key == "ts") ts = value;
            else if (key == "h1") signatures.Add(value);
        }
        if (ts == null || signatures.Count == 0 || !long.TryParse(ts, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)) return false;
        if ((now - DateTimeOffset.FromUnixTimeSeconds(seconds)).Duration() > Tolerance) return false;

        var expected = Sign(ts, rawBody, secret);
        var expectedBytes = Encoding.ASCII.GetBytes(expected);
        return signatures.Any(s => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(s.ToLowerInvariant()), expectedBytes));
    }

    /// <summary>The h1 value for a timestamp and body (also used by the tests and the local mock).</summary>
    public static string Sign(string ts, string rawBody, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{ts}:{rawBody}"))).ToLowerInvariant();
    }
}

/// <summary>One line of a Paddle transaction or subscription: which price, how many.</summary>
public record PaddleItem(string PriceId, int Quantity);

/// <summary>
/// The parts of a Paddle webhook the billing needs, read without trusting anything else in it.
/// Kind is "transaction" or "subscription" (from the event type's prefix).
/// </summary>
public record PaddleEvent(
    string EventId,
    string EventType,
    DateTime OccurredUtc,
    string Kind,
    string ObjectId,
    string? Status,
    string? CustomerId,
    string? SubscriptionId,
    string? OwnerId,
    IReadOnlyList<PaddleItem> Items,
    DateTime? PeriodStartUtc,
    DateTime? PeriodEndUtc,
    bool CancelScheduled,
    string? Currency,
    int? SubtotalCents,
    int? TaxCents,
    int? TotalCents,
    string? InvoiceNumber,
    string? Origin);

/// <summary>Reads Paddle Billing webhooks (tested in PaddleEventsTests).</summary>
public static class PaddleEvents
{
    /// <summary>The event types the billing acts on; others are acknowledged and ignored.</summary>
    public static readonly IReadOnlySet<string> Handled = new HashSet<string>(StringComparer.Ordinal)
    {
        "transaction.completed", "transaction.payment_failed",
        "subscription.created", "subscription.activated", "subscription.updated", "subscription.past_due",
        "subscription.canceled", "subscription.resumed", "subscription.paused"
    };

    /// <summary>Null when it isn't a Paddle event (no event id, type or data object).</summary>
    public static PaddleEvent? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (Str(root, "event_id") is not { } eventId || Str(root, "event_type") is not { } type
            || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object || Str(data, "id") is not { } id) return null;

        var kind = type.StartsWith("subscription.", StringComparison.Ordinal) ? "subscription" : "transaction";
        var items = new List<PaddleItem>();
        if (data.TryGetProperty("items", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var it in list.EnumerateArray())
            {
                var priceId = it.TryGetProperty("price", out var price) && price.ValueKind == JsonValueKind.Object ? Str(price, "id") : Str(it, "price_id");
                var qty = it.TryGetProperty("quantity", out var q) && q.TryGetInt32(out var n) ? n : 1;
                if (priceId != null) items.Add(new PaddleItem(priceId, qty));
            }
        }

        var period = kind == "subscription" ? Obj(data, "current_billing_period") : Obj(data, "billing_period");
        var totals = Obj(Obj(data, "details"), "totals");
        var custom = Obj(data, "custom_data");
        var scheduled = Obj(data, "scheduled_change");

        return new PaddleEvent(
            eventId, type, Date(Str(root, "occurred_at")) ?? DateTime.UtcNow, kind, id,
            Str(data, "status"), Str(data, "customer_id"),
            kind == "subscription" ? id : Str(data, "subscription_id"),
            custom is { } c ? Str(c, "owner_id") : null,
            items,
            period is { } p1 ? Date(Str(p1, "starts_at")) : null,
            period is { } p2 ? Date(Str(p2, "ends_at")) : null,
            scheduled is { } s && Str(s, "action") == "cancel",
            Str(data, "currency_code") ?? (totals is { } t0 ? Str(t0, "currency_code") : null),
            totals is { } t1 ? Cents(Str(t1, "subtotal")) : null,
            totals is { } t2 ? Cents(Str(t2, "tax")) : null,
            totals is { } t3 ? Cents(Str(t3, "total")) : null,
            Str(data, "invoice_number"),
            Str(data, "origin"));
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static JsonElement? Obj(JsonElement? e, string name) =>
        e is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;

    private static DateTime? Date(string? s) =>
        DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d : null;

    /// <summary>Paddle sends amounts as strings in the lowest unit ("9720" = €97.20).</summary>
    private static int? Cents(string? s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
}
