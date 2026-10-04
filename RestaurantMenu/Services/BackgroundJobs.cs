using System.Threading.Channels;

namespace RestaurantMenu.Services;

/// <summary>
/// Small jobs run after the response (text messages), each in its own DI scope. Bounded so
/// a flood can't eat memory; what's queued at shutdown is lost, which is acceptable for
/// best-effort notifications.
/// </summary>
public class BackgroundJobs
{
    private readonly Channel<(string Name, Func<IServiceProvider, CancellationToken, Task> Work)> _channel =
        Channel.CreateBounded<(string, Func<IServiceProvider, CancellationToken, Task>)>(new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly ILogger<BackgroundJobs> _logger;

    public BackgroundJobs(ILogger<BackgroundJobs> logger) => _logger = logger;

    public void Enqueue(string name, Func<IServiceProvider, CancellationToken, Task> work)
    {
        if (!_channel.Writer.TryWrite((name, work))) _logger.LogWarning("Background queue full; {Job} dropped", name);
    }

    public IAsyncEnumerable<(string Name, Func<IServiceProvider, CancellationToken, Task> Work)> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}

public class BackgroundJobsWorker : BackgroundService
{
    private readonly BackgroundJobs _jobs;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<BackgroundJobsWorker> _logger;

    public BackgroundJobsWorker(BackgroundJobs jobs, IServiceScopeFactory scopes, ILogger<BackgroundJobsWorker> logger)
    {
        _jobs = jobs;
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var (name, work) in _jobs.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopes.CreateScope();
                await work(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Background job {Job} failed", name);
            }
        }
    }
}
