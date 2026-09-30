using Argoscope.Application.Collection;
using Argoscope.Domain.Common;
using Argoscope.Domain.Memberships;
using Argoscope.Domain.Portfolios;
using Argoscope.Domain.Repositories;
using Argoscope.Domain.Snapshots;
using Argoscope.GitHub;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Argoscope.Infrastructure.Scheduling;

/// <summary>Configuration for the daily collection job.</summary>
public sealed class DailyCollectionOptions
{
    /// <summary>Cron-style interval in hours. Default 24h.</summary>
    public int IntervalHours { get; set; } = 24;

    /// <summary>UTC hour of day when the run should start (0-23). Default 03:00.</summary>
    public int StartHourUtc { get; set; } = 3;

    /// <summary>If true, run once on startup before entering the loop.</summary>
    public bool RunOnStartup { get; set; } = true;
}

/// <summary>
/// Hosted service that runs a daily collection across every membership in
/// every portfolio. Implementation is a thin replacement for Hangfire's
/// recurring-job contract; the sibling `dotnet-platform-libs` exposes
/// `IRecurringJobRegistry`, but it is not yet published, so the local adapter
/// uses <see cref="BackgroundService"/> directly.
/// </summary>
public sealed class DailyCollectionHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly DailyCollectionOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<DailyCollectionHostedService> _logger;

    public DailyCollectionHostedService(
        IServiceScopeFactory scopes,
        IOptions<DailyCollectionOptions> options,
        IClock clock,
        ILogger<DailyCollectionHostedService> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.RunOnStartup)
        {
            try
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Startup collection run failed; entering normal loop.");
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var nextRun = NextRunUtc(_clock.UtcNow);
            var delay = nextRun - _clock.UtcNow;
            if (delay < TimeSpan.Zero) delay = TimeSpan.FromMinutes(1);
            try
            {
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Daily collection run failed; will retry on next interval.");
            }
        }
    }

    private DateTimeOffset NextRunUtc(DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var candidate = today.ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromHours(_options.StartHourUtc)), DateTimeKind.Utc);
        if (candidate <= now.UtcDateTime)
        {
            candidate = candidate.AddDays(1);
        }
        return new DateTimeOffset(candidate, TimeSpan.Zero);
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        var portfolioRepo = scope.ServiceProvider.GetRequiredService<IPortfolioRepository>();
        var repoStore = scope.ServiceProvider.GetRequiredService<IRepositoryStore>();
        var memberships = scope.ServiceProvider.GetRequiredService<IMembershipStore>();
        var collection = scope.ServiceProvider.GetRequiredService<CollectionService>();
        var portfolios = await portfolioRepo.ListAllAsync(cancellationToken).ConfigureAwait(false);
        var now = _clock.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var totalSnapshots = 0;
        foreach (var portfolio in portfolios)
        {
            var ms = await memberships.ListByPortfolioAsync(portfolio, cancellationToken).ConfigureAwait(false);
            foreach (var m in ms)
            {
                var repo = await repoStore.FindAsync(m.RepositoryId, cancellationToken).ConfigureAwait(false);
                if (repo is null) continue;
                try
                {
                    var result = await collection.RunAsync(new CollectionRequest(portfolio, repo.OwnerLogin, repo.Name, today, now), cancellationToken).ConfigureAwait(false);
                    totalSnapshots += result.SnapshotsWritten;
                    if (result.MetricsStatus is ProviderResultStatus.RateLimited
                        or ProviderResultStatus.Unauthorized
                        or ProviderResultStatus.Forbidden
                        or ProviderResultStatus.NotFound
                        or ProviderResultStatus.Unavailable
                        or ProviderResultStatus.Malformed)
                    {
                        _logger.LogWarning("Collection for {Owner}/{Name} returned {Status}", repo.OwnerLogin, repo.Name, result.MetricsStatus);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Collection for {Owner}/{Name} threw", repo.OwnerLogin, repo.Name);
                }
            }
        }
        _logger.LogInformation("Daily collection finished. {Count} snapshot rows written.", totalSnapshots);
    }
}
