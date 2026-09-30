using Argoscope.Application.Collection;
using Argoscope.Application.Packages;
using Argoscope.Domain.Common;
using Argoscope.Domain.Packages;
using Argoscope.Domain.Repositories;
using Argoscope.Domain.Snapshots;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Argoscope.Infrastructure.Scheduling;

/// <summary>
/// Configuration for the daily package-adoption collection job.
/// Independent of the GitHub collection options so the two schedules
/// can be tuned separately.
/// </summary>
public sealed class PackageCollectionOptions
{
    /// <summary>Cron-style interval in hours. Default 24h.</summary>
    public int IntervalHours { get; set; } = 24;

    /// <summary>UTC hour of day when the run should start (0-23). Default 04:00 (an hour after the GitHub collection).</summary>
    public int StartHourUtc { get; set; } = 4;

    /// <summary>If true, run once on startup before entering the loop.</summary>
    public bool RunOnStartup { get; set; } = false;
}

/// <summary>
/// Hosted service that walks every linked package association once a
/// day and asks the registered provider for new observations. The
/// failure policy is identical to the GitHub collection: transient
/// provider failures do not overwrite last-good observations and the
/// association transitions to <see
/// cref="PackageAssociationStatus.AttentionRequired"/> only on
/// not-found responses.
/// </summary>
public sealed class PackageCollectionHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly PackageCollectionOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<PackageCollectionHostedService> _logger;

    public PackageCollectionHostedService(
        IServiceScopeFactory scopes,
        IOptions<PackageCollectionOptions> options,
        IClock clock,
        ILogger<PackageCollectionHostedService> logger)
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
                _logger.LogError(ex, "Startup package collection run failed; entering normal loop.");
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
                _logger.LogError(ex, "Daily package collection run failed; will retry on next interval.");
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
        var repoStore = scope.ServiceProvider.GetRequiredService<IRepositoryStore>();
        var associations = scope.ServiceProvider.GetRequiredService<IPackageAssociationStore>();
        var collection = scope.ServiceProvider.GetRequiredService<PackageCollectionService>();

        var now = _clock.UtcNow;
        // Iterate every linked association in the system. The hosted
        // service is intentionally cross-portfolio because the design
        // treats package adoption as repository-scoped, not
        // portfolio-scoped (a single repository may be added to
        // multiple portfolios with different roles).
        var allRepos = await scope.ServiceProvider
            .GetRequiredService<IPortfolioRepository>()
            .ListAllAsync(cancellationToken)
            .ConfigureAwait(false);
        var totalWritten = 0;
        foreach (var portfolioId in allRepos)
        {
            var repoList = await repoStore.ListByPortfolioAsync(portfolioId, cancellationToken).ConfigureAwait(false);
            foreach (var repo in repoList)
            {
                var assocs = await associations.ListByRepositoryAsync(repo.Id, cancellationToken).ConfigureAwait(false);
                foreach (var assoc in assocs)
                {
                    if (assoc.Status == PackageAssociationStatus.Removed) continue;
                    try
                    {
                        var result = await collection.RunAsync(
                            new PackageCollectionRequest(
                                repo.Id, assoc.Id, assoc.Provider, assoc.Coordinate,
                                assoc.DefaultUnit, assoc.DefaultWindow, now),
                            cancellationToken).ConfigureAwait(false);
                        totalWritten += result.ObservationsWritten;
                        if (result.PageStatus is ProviderResultStatus.RateLimited
                            or ProviderResultStatus.Unauthorized
                            or ProviderResultStatus.Forbidden
                            or ProviderResultStatus.Unavailable
                            or ProviderResultStatus.Malformed
                            or ProviderResultStatus.NotFound)
                        {
                            _logger.LogWarning(
                                "Package collection for {Provider}/{Coordinate} returned {Status}",
                                assoc.Provider, assoc.Coordinate, result.PageStatus);
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex, "Package collection for {Provider}/{Coordinate} threw",
                            assoc.Provider, assoc.Coordinate);
                    }
                }
            }
        }
        _logger.LogInformation("Daily package collection finished. {Count} observation rows written.", totalWritten);
    }
}
