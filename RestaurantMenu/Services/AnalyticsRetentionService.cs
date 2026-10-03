using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

/// <summary>
/// Deletes menu events older than Analytics:RetentionDays (default 400, so a year can
/// always be compared with the one before it), and table orders older than
/// Orders:RetentionDays (default 400; orders can hold guests' notes). Runs shortly after
/// startup and then daily.
/// Idempotent, so several instances running it at once is harmless.
/// </summary>
public class AnalyticsRetentionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AnalyticsRetentionService> _logger;
    private readonly int _retentionDays;
    private readonly int _orderRetentionDays;

    public AnalyticsRetentionService(IServiceScopeFactory scopes, ILogger<AnalyticsRetentionService> logger, IConfiguration config)
    {
        _scopes = scopes;
        _logger = logger;
        _retentionDays = Math.Max(30, config.GetValue("Analytics:RetentionDays", 400));
        _orderRetentionDays = Math.Max(30, config.GetValue("Orders:RetentionDays", 400));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var cutoff = DateTime.UtcNow.AddDays(-_retentionDays);
                    var deleted = await db.MenuEvents.Where(e => e.CreatedUtc < cutoff).ExecuteDeleteAsync(stoppingToken);
                    if (deleted > 0)
                    {
                        _logger.LogInformation("Analytics retention: deleted {Count} menu events older than {Days} days", deleted, _retentionDays);
                    }

                    var orderCutoff = DateTime.UtcNow.AddDays(-_orderRetentionDays);
                    // IgnoreQueryFilters: orders of deleted branches age out too. Items go by cascade.
                    var orders = await db.Orders.IgnoreQueryFilters().Where(o => o.CreatedUtc < orderCutoff).ExecuteDeleteAsync(stoppingToken);
                    if (orders > 0)
                    {
                        _logger.LogInformation("Order retention: deleted {Count} orders older than {Days} days", orders, _orderRetentionDays);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Analytics retention run failed; retrying tomorrow");
                }

                await Task.Delay(TimeSpan.FromDays(1), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }
}
