using System.Threading.Channels;

namespace RestaurantMenu.Services;

/// <summary>
/// Emails sent after the response, in the background. Used where the page must answer the
/// same way, at the same speed, whatever happens: "forgot password" must not reveal
/// through a slower reply that an account exists. Bounded, so a flood can't eat memory;
/// what's still queued at shutdown is lost (the person can ask again).
/// </summary>
public class EmailQueue
{
    private readonly Channel<EmailMessage> _channel =
        Channel.CreateBounded<EmailMessage>(new BoundedChannelOptions(200) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly ILogger<EmailQueue> _logger;

    public EmailQueue(ILogger<EmailQueue> logger) => _logger = logger;

    public void Enqueue(EmailMessage message)
    {
        if (!_channel.Writer.TryWrite(message))
            _logger.LogWarning("Email queue is full; \"{Subject}\" was dropped", message.Subject);
    }

    public IAsyncEnumerable<EmailMessage> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}

public class EmailQueueWorker : BackgroundService
{
    private readonly EmailQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<EmailQueueWorker> _logger;

    public EmailQueueWorker(EmailQueue queue, IServiceScopeFactory scopes, ILogger<EmailQueueWorker> logger)
    {
        _queue = queue;
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<EmailService>().SendAsync(message, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Queued email \"{Subject}\" could not be sent", message.Subject);
            }
        }
    }
}
