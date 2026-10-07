namespace RestaurantMenu.Tests;

public class PaddleEventsTests
{
    private const string Secret = "pdl_ntfset_test_secret";
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    private static string Ts(DateTimeOffset t) => t.ToUnixTimeSeconds().ToString();
    private static string Header(string body, DateTimeOffset at, string secret = Secret) =>
        $"ts={Ts(at)};h1={PaddleSignature.Sign(Ts(at), body, secret)}";

    private const string Completed = """
        {"event_id":"evt_01","event_type":"transaction.completed","occurred_at":"2026-10-07T12:00:00.000Z","notification_id":"ntf_1",
         "data":{"id":"txn_01abc","status":"completed","customer_id":"ctm_1","subscription_id":"sub_1","origin":"web","invoice_number":"325-10001",
                 "currency_code":"EUR","custom_data":{"owner_id":"owner-1"},
                 "items":[{"price":{"id":"pri_menu_m"},"quantity":2},{"price":{"id":"pri_book_m"},"quantity":2}],
                 "billing_period":{"starts_at":"2026-10-07T12:00:00Z","ends_at":"2026-11-07T12:00:00Z"},
                 "details":{"totals":{"subtotal":"4400","tax":"880","total":"5280","currency_code":"EUR"}}}}
        """;

    [Fact]
    public void A_correct_signature_passes()
    {
        Assert.True(PaddleSignature.IsValid(Header(Completed, Now), Completed, Secret, Now));
        Assert.True(PaddleSignature.IsValid(Header(Completed, Now.AddMinutes(-4)), Completed, Secret, Now));
    }

    [Fact]
    public void Tampered_body_wrong_secret_or_missing_header_fail()
    {
        var header = Header(Completed, Now);
        Assert.False(PaddleSignature.IsValid(header, Completed.Replace("5280", "1"), Secret, Now));
        Assert.False(PaddleSignature.IsValid(Header(Completed, Now, "other"), Completed, Secret, Now));
        Assert.False(PaddleSignature.IsValid(null, Completed, Secret, Now));
        Assert.False(PaddleSignature.IsValid("ts=abc;h1=00", Completed, Secret, Now));
        Assert.False(PaddleSignature.IsValid(header, Completed, null, Now));
    }

    [Fact]
    public void An_old_signature_is_refused_so_captured_requests_cant_be_replayed_later()
    {
        Assert.False(PaddleSignature.IsValid(Header(Completed, Now.AddMinutes(-6)), Completed, Secret, Now));
    }

    [Fact]
    public void During_secret_rotation_any_listed_signature_is_enough()
    {
        var good = PaddleSignature.Sign(Ts(Now), Completed, Secret);
        Assert.True(PaddleSignature.IsValid($"ts={Ts(Now)};h1=deadbeef;h1={good}", Completed, Secret, Now));
    }

    [Fact]
    public void A_completed_transaction_is_read_with_period_items_owner_and_totals()
    {
        var e = PaddleEvents.Parse(Completed)!;
        Assert.Equal("evt_01", e.EventId);
        Assert.Equal("transaction", e.Kind);
        Assert.Equal("txn_01abc", e.ObjectId);
        Assert.Equal("sub_1", e.SubscriptionId);
        Assert.Equal("owner-1", e.OwnerId);
        Assert.Equal(new[] { new PaddleItem("pri_menu_m", 2), new PaddleItem("pri_book_m", 2) }, e.Items);
        Assert.Equal(new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc), e.PeriodStartUtc);
        Assert.Equal(new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc), e.PeriodEndUtc);
        Assert.Equal((4400, 880, 5280), (e.SubtotalCents, e.TaxCents, e.TotalCents));
        Assert.Equal("325-10001", e.InvoiceNumber);
        Assert.Equal("web", e.Origin);
        Assert.Contains(e.EventType, PaddleEvents.Handled);
    }

    [Fact]
    public void A_subscription_event_uses_its_own_id_period_and_scheduled_cancel()
    {
        const string json = """
            {"event_id":"evt_02","event_type":"subscription.past_due","occurred_at":"2026-11-07T12:05:00Z",
             "data":{"id":"sub_1","status":"past_due","customer_id":"ctm_1","custom_data":{"owner_id":"owner-1"},
                     "items":[{"price":{"id":"pri_menu_m"},"quantity":2}],
                     "current_billing_period":{"starts_at":"2026-11-07T12:00:00Z","ends_at":"2026-12-07T12:00:00Z"},
                     "scheduled_change":{"action":"cancel","effective_at":"2026-12-07T12:00:00Z"}}}
            """;
        var e = PaddleEvents.Parse(json)!;
        Assert.Equal("subscription", e.Kind);
        Assert.Equal("sub_1", e.SubscriptionId);
        Assert.Equal("past_due", e.Status);
        Assert.True(e.CancelScheduled);
        Assert.Equal(new DateTime(2026, 12, 7, 12, 0, 0, DateTimeKind.Utc), e.PeriodEndUtc);
    }

    [Fact]
    public void Anything_that_isnt_a_paddle_event_is_null_and_unknown_types_are_not_handled()
    {
        Assert.Null(PaddleEvents.Parse("{}"));
        Assert.Null(PaddleEvents.Parse("""{"event_id":"e","event_type":"x"}"""));
        Assert.DoesNotContain("customer.updated", PaddleEvents.Handled);
    }
}
