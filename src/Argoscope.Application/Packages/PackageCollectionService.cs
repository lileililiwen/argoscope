using Argoscope.Domain.Common;
using Argoscope.Domain.Packages;
using Argoscope.Domain.Snapshots;
using Argoscope.Packages;
using Microsoft.Extensions.Logging;

namespace Argoscope.Application.Packages;

/// <summary>
/// Orchestrates one provider call per (association). Failure handling
/// follows the same last-good policy as the GitHub collection service:
/// a failed run does not overwrite previously stored complete
/// observations; the run result reports the failure status and the
/// preserved count, and the association is transitioned to
/// <see cref="PackageAssociationStatus.AttentionRequired"/> on
/// not-found responses so the dashboard surfaces the problem. The
/// collection service is pure with respect to the supplied stores.
/// </summary>
public sealed class PackageCollectionService
{
    private readonly PackageProviderRegistry _providers;
    private readonly IPackageAssociationStore _associations;
    private readonly IPackageObservationStore _observations;
    private readonly ILogger<PackageCollectionService> _logger;
    private readonly IClock _clock;

    public PackageCollectionService(
        PackageProviderRegistry providers,
        IPackageAssociationStore associations,
        IPackageObservationStore observations,
        ILogger<PackageCollectionService> logger,
        IClock clock)
    {
        _providers = providers;
        _associations = associations;
        _observations = observations;
        _logger = logger;
        _clock = clock;
    }

    public async Task<PackageCollectionRunResult> RunAsync(PackageCollectionRequest request, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var provider = _providers.Resolve(request.Provider);
        if (provider is null)
        {
            // No provider registered for this kind: this is a
            // configuration problem, not a transient failure.
            _logger.LogWarning("No provider registered for {Provider}; skipping {Coordinate}.", request.Provider, request.Coordinate);
            return new PackageCollectionRunResult(
                request.AssociationId, 0, 0,
                ProviderResultStatus.Unavailable, ProviderResultStatus.Unavailable,
                "provider-not-registered", now);
        }

        // 1. Probe the package metadata. A not-found response transitions
        //    the association to attention-required and aborts the run.
        var probe = await provider.ProbeAsync(request.Coordinate, cancellationToken).ConfigureAwait(false);
        if (probe.Status == ProviderResultStatus.NotFound)
        {
            var association = await _associations.FindAsync(request.AssociationId, cancellationToken).ConfigureAwait(false);
            if (association is not null)
            {
                association.MarkAttentionRequired("package-not-found", now);
                await _associations.UpdateAsync(association, cancellationToken).ConfigureAwait(false);
            }
            return new PackageCollectionRunResult(
                request.AssociationId, 0, 0,
                probe.Status, probe.Status, probe.DiagnosticCode, now);
        }
        if (probe.Status is ProviderResultStatus.RateLimited
            or ProviderResultStatus.Unauthorized
            or ProviderResultStatus.Forbidden
            or ProviderResultStatus.Unavailable
            or ProviderResultStatus.Malformed)
        {
            // Transient failure: do not modify the association; preserve
            // the last-good observations; the run result surfaces the
            // status so the dashboard can mark the series stale.
            return new PackageCollectionRunResult(
                request.AssociationId, 0, 0,
                probe.Status, probe.Status, probe.DiagnosticCode, now);
        }

        // 2. Walk the observation pages idempotently.
        var observationsWritten = 0;
        var observationsPreserved = 0;
        var lastPageStatus = ProviderResultStatus.Available;
        string? lastDiagnostic = null;
        string? cursor = null;
        var pageCount = 0;
        const int maxPages = 100;
        var sinceUtc = now.AddDays(-365);

        while (pageCount < maxPages)
        {
            var page = await provider.GetObservationsAsync(
                request.Coordinate, request.Unit, request.Window, sinceUtc, cursor, cancellationToken)
                .ConfigureAwait(false);
            lastPageStatus = page.Status;
            lastDiagnostic = page.DiagnosticCode;

            if (page.Status is not (ProviderResultStatus.Available or ProviderResultStatus.Partial))
            {
                // Transient failure mid-stream: stop the page walk but
                // preserve the partial upserts we already wrote.
                break;
            }
            if (page.Items.Count == 0)
            {
                break;
            }
            foreach (var obs in page.Items)
            {
                var existing = await _observations.FindAsync(
                    request.AssociationId, obs.Unit, obs.Window, obs.WindowStartUtc, obs.WindowEndUtc, cancellationToken)
                    .ConfigureAwait(false);
                if (existing is not null)
                {
                    // Last-good: do not overwrite a previously stored
                    // complete observation with a partial one. A new
                    // complete observation replaces the prior one.
                    if (existing.IsComplete)
                    {
                        observationsPreserved++;
                        continue;
                    }
                }
                var entity = new PackageObservation(
                    request.AssociationId,
                    request.Provider,
                    provider.ProviderVersion,
                    obs.Unit,
                    obs.Window,
                    obs.WindowStartUtc,
                    obs.WindowEndUtc,
                    obs.Value,
                    page.Status,
                    isComplete: true,
                    obs.ObservedAtUtc,
                    now,
                    page.DiagnosticCode);
                await _observations.UpsertAsync(entity, cancellationToken).ConfigureAwait(false);
                observationsWritten++;
            }
            cursor = page.NextCursor;
            pageCount++;
            if (cursor is null) break;
        }

        // 3. Update the association status: if the probe and the page
        //    walk both succeeded, mark the association linked; if a
        //    transient failure occurred, keep the prior status and let
        //    the next run retry.
        var associationForUpdate = await _associations.FindAsync(request.AssociationId, cancellationToken).ConfigureAwait(false);
        if (associationForUpdate is not null && associationForUpdate.Status == PackageAssociationStatus.AttentionRequired
            && lastPageStatus == ProviderResultStatus.Available)
        {
            associationForUpdate.MarkLinked(now);
            await _associations.UpdateAsync(associationForUpdate, cancellationToken).ConfigureAwait(false);
        }

        return new PackageCollectionRunResult(
            request.AssociationId, observationsWritten, observationsPreserved,
            probe.Status, lastPageStatus, lastDiagnostic, now);
    }
}
