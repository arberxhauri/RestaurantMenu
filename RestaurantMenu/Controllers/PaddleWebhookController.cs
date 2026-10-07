using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using RestaurantMenu.Filters;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;
using RestaurantMenu.Services;

namespace RestaurantMenu.Controllers;

/// <summary>
/// POST /billing/webhooks/paddle, Paddle's notifications. The signature is checked against the
/// raw body before anything is read or stored; a valid event goes into the billing inbox (a
/// repeat of the same event id is a no-op) and the answer is an immediate 200. BillingWorker
/// applies it within 30 seconds. Event types the billing doesn't use are acknowledged and dropped.
/// </summary>
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[NoIndex]
public class PaddleWebhookController : Controller
{
    private const int MaxBody = 512 * 1024;

    private readonly ApplicationDbContext _db;
    private readonly PaddleOptions _options;
    private readonly ILogger<PaddleWebhookController> _logger;

    public PaddleWebhookController(ApplicationDbContext db, IOptions<PaddleOptions> options, ILogger<PaddleWebhookController> logger)
    {
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    [HttpPost("billing/webhooks/paddle")]
    [EnableRateLimiting("webhooks")]
    [RequestSizeLimit(MaxBody)]
    public async Task<IActionResult> Receive()
    {
        if (string.IsNullOrEmpty(_options.WebhookSecret)) return NotFound(); // Paddle isn't set up here

        string body;
        using (var reader = new StreamReader(Request.Body))
            body = await reader.ReadToEndAsync();

        if (!PaddleSignature.IsValid(Request.Headers["Paddle-Signature"], body, _options.WebhookSecret, DateTimeOffset.UtcNow))
        {
            _logger.LogWarning("Paddle webhook refused: bad or missing signature from {Ip}", HttpContext.Connection.RemoteIpAddress);
            return Unauthorized();
        }

        PaddleEvent? ev;
        try { ev = PaddleEvents.Parse(body); }
        catch (System.Text.Json.JsonException) { ev = null; }
        if (ev == null) return BadRequest();
        if (!PaddleEvents.Handled.Contains(ev.EventType)) return Ok();

        _db.BillingEvents.Add(new BillingEvent
        {
            Provider = BillingProvider.Paddle,
            EventId = ev.EventId,
            Type = ev.EventType switch
            {
                "transaction.completed" => BillingEventType.PaymentSucceeded,
                "transaction.payment_failed" => BillingEventType.PaymentFailed,
                "subscription.canceled" => BillingEventType.SubscriptionCanceled,
                _ => BillingEventType.SubscriptionUpdated
            },
            PayloadJson = body,
            ReceivedUtc = DateTime.UtcNow
        });
        try
        {
            await _db.SaveChangesAsync();
            _logger.LogInformation("Paddle webhook {Type} {EventId} stored", ev.EventType, ev.EventId);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Already stored: Paddle retries until it gets a 2xx, and replays are harmless.
        }
        return Ok();
    }
}
