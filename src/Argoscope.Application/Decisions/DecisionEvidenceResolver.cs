using Argoscope.Application.Collection;
using Argoscope.Application.Packages;
using Argoscope.Domain.Common;
using Argoscope.Domain.Decisions;
using Argoscope.Domain.Packages;
using Argoscope.Domain.Portfolios;
using Argoscope.Domain.Repositories;
using Argoscope.Domain.Snapshots;

namespace Argoscope.Application.Decisions;

/// <summary>
/// Summary of a resolved evidence target. The <see cref="Destination"/>
/// is the human-readable locator (e.g. <c>example/demo-app @ 2024-04-12</c>)
/// that the UI shows when the reference is still live.
/// </summary>
public sealed record DecisionEvidenceTarget(
    DecisionEvidenceResolution Resolution,
    string Destination,
    DateOnly? EvidenceDate);

/// <summary>
/// Cross-aggregate evidence reference checker. The
/// <see cref="DecisionService"/> calls into this to validate that each
/// typed reference points at a row that exists in the same portfolio
/// and was created during the open decision window. The resolver never
/// mutates state and never throws for legitimate not-found cases — it
/// returns <see cref="DecisionEvidenceResolution.Unresolved"/> instead
/// so the API can keep the reference as a broken-link marker.
/// </summary>
public sealed class DecisionEvidenceResolver
{
    private readonly IPortfolioRepository _portfolios;
    private readonly IMembershipStore _memberships;
    private readonly IMetricSnapshotStore _snapshots;
    private readonly IPackageAssociationStore _packageAssociations;
    private readonly IPackageObservationStore _packageObservations;

    public DecisionEvidenceResolver(
        IPortfolioRepository portfolios,
        IMembershipStore memberships,
        IMetricSnapshotStore snapshots,
        IPackageAssociationStore packageAssociations,
        IPackageObservationStore packageObservations)
    {
        _portfolios = portfolios;
        _memberships = memberships;
        _snapshots = snapshots;
        _packageAssociations = packageAssociations;
        _packageObservations = packageObservations;
    }

    /// <summary>
    /// Validate a single reference. Returns a structured result that the
    /// caller turns into a <see cref="DecisionEvidenceDto"/>. The
    /// <paramref name="portfolioId"/> is the decision's owning portfolio
    /// — a reference that points at a row owned by a different portfolio
    /// is rejected.
    /// </summary>
    public async Task<DecisionEvidenceTarget> ResolveAsync(
        Id<Portfolio> portfolioId,
        DecisionEvidenceKind kind,
        Guid referenceId,
        CancellationToken cancellationToken)
    {
        switch (kind)
        {
            case DecisionEvidenceKind.Snapshot:
                return await ResolveSnapshotAsync(portfolioId, referenceId, cancellationToken)
                    .ConfigureAwait(false);
            case DecisionEvidenceKind.PackageObservation:
                return await ResolvePackageObservationAsync(portfolioId, referenceId, cancellationToken)
                    .ConfigureAwait(false);
            case DecisionEvidenceKind.CommercialSignal:
                // Not yet implemented in argoscope; surface the
                // reference as unresolved with a descriptive
                // destination so the UI can still display the
                // broken-link marker.
                return new DecisionEvidenceTarget(
                    DecisionEvidenceResolution.Unresolved,
                    "commercial-signal (not yet collected)",
                    null);
            default:
                return new DecisionEvidenceTarget(
                    DecisionEvidenceResolution.Unresolved,
                    $"unknown-kind:{kind}",
                    null);
        }
    }

    private async Task<DecisionEvidenceTarget> ResolveSnapshotAsync(
        Id<Portfolio> portfolioId, Guid referenceId, CancellationToken cancellationToken)
    {
        var snapshot = await _snapshots
            .FindByIdAsync(Id<MetricSnapshot>.From(referenceId), cancellationToken)
            .ConfigureAwait(false);
        if (snapshot is null)
        {
            return new DecisionEvidenceTarget(
                DecisionEvidenceResolution.Unresolved,
                $"snapshot:{referenceId}",
                null);
        }
        if (!await IsRepositoryInPortfolioAsync(portfolioId, snapshot.RepositoryId, cancellationToken)
            .ConfigureAwait(false))
        {
            return new DecisionEvidenceTarget(
                DecisionEvidenceResolution.Unresolved,
                $"snapshot:{referenceId} (cross-portfolio)",
                snapshot.MetricDate);
        }
        return new DecisionEvidenceTarget(
            DecisionEvidenceResolution.Resolved,
            $"snapshot @ {snapshot.MetricDate:yyyy-MM-dd} ({snapshot.MetricName})",
            snapshot.MetricDate);
    }

    private async Task<DecisionEvidenceTarget> ResolvePackageObservationAsync(
        Id<Portfolio> portfolioId, Guid referenceId, CancellationToken cancellationToken)
    {
        var observation = await _packageObservations
            .FindByIdAsync(Id<PackageObservation>.From(referenceId), cancellationToken)
            .ConfigureAwait(false);
        if (observation is null)
        {
            return new DecisionEvidenceTarget(
                DecisionEvidenceResolution.Unresolved,
                $"package-observation:{referenceId}",
                null);
        }
        var association = await _packageAssociations
            .FindAsync(observation.PackageAssociationId, cancellationToken)
            .ConfigureAwait(false);
        if (association is null)
        {
            return new DecisionEvidenceTarget(
                DecisionEvidenceResolution.Unresolved,
                $"package-observation:{referenceId} (orphan)",
                null);
        }
        if (!await IsRepositoryInPortfolioAsync(portfolioId, association.RepositoryId, cancellationToken)
            .ConfigureAwait(false))
        {
            return new DecisionEvidenceTarget(
                DecisionEvidenceResolution.Unresolved,
                $"package-observation:{referenceId} (cross-portfolio)",
                null);
        }
        return new DecisionEvidenceTarget(
            DecisionEvidenceResolution.Resolved,
            $"package-observation @ {observation.WindowStartUtc:yyyy-MM-dd}",
            DateOnly.FromDateTime(observation.WindowStartUtc.UtcDateTime));
    }

    private async Task<bool> IsRepositoryInPortfolioAsync(
        Id<Portfolio> portfolioId, Id<Repository> repositoryId, CancellationToken cancellationToken)
    {
        var portfolio = await _portfolios.FindAsync(portfolioId, cancellationToken).ConfigureAwait(false);
        if (portfolio is null) return false;
        var membership = await _memberships
            .FindByRepositoryAsync(portfolioId, repositoryId, cancellationToken)
            .ConfigureAwait(false);
        return membership is not null;
    }
}
