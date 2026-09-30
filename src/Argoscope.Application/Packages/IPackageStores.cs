using Argoscope.Domain.Common;
using Argoscope.Domain.Packages;
using Argoscope.Domain.Repositories;
using Argoscope.Domain.Snapshots;

namespace Argoscope.Application.Packages;

/// <summary>
/// Inputs for a single package-association collection run.
/// </summary>
public sealed record PackageCollectionRequest(
    Id<Repository> RepositoryId,
    Id<PackageAssociation> AssociationId,
    PackageProvider Provider,
    string Coordinate,
    PackageUnit Unit,
    PackageWindow Window,
    DateTimeOffset AsOfUtc);

/// <summary>Result of a single package-association collection run.</summary>
public sealed record PackageCollectionRunResult(
    Id<PackageAssociation> AssociationId,
    int ObservationsWritten,
    int ObservationsPreserved,
    ProviderResultStatus MetadataStatus,
    ProviderResultStatus PageStatus,
    string? DiagnosticCode,
    DateTimeOffset RunAtUtc);

/// <summary>
/// Persistence contracts for package associations and observations. The
/// infrastructure layer implements these with EF Core; tests implement
/// them with in-memory stores.
/// </summary>
public interface IPackageAssociationStore
{
    Task<PackageAssociation?> FindAsync(Id<PackageAssociation> id, CancellationToken cancellationToken);
    Task<PackageAssociation?> FindByRepositoryAndCoordinateAsync(
        Id<Repository> repositoryId, PackageProvider provider, string coordinate, CancellationToken cancellationToken);
    Task<IReadOnlyList<PackageAssociation>> ListByRepositoryAsync(Id<Repository> repositoryId, CancellationToken cancellationToken);
    Task<PackageAssociation> AddAsync(PackageAssociation association, CancellationToken cancellationToken);
    Task UpdateAsync(PackageAssociation association, CancellationToken cancellationToken);
    Task RemoveAsync(Id<PackageAssociation> id, CancellationToken cancellationToken);
}

public interface IPackageObservationStore
{
    /// <summary>Find the observation for the (association, unit, window, window) tuple — the idempotency key.</summary>
    Task<PackageObservation?> FindAsync(
        Id<PackageAssociation> associationId,
        PackageUnit unit,
        PackageWindow window,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Upsert one observation. If a previously stored complete observation
    /// exists for the same tuple and the new one is incomplete, the prior
    /// row is preserved (last-good retention).
    /// </summary>
    Task<PackageObservation> UpsertAsync(PackageObservation observation, CancellationToken cancellationToken);

    Task<IReadOnlyList<PackageObservation>> ListByAssociationAsync(
        Id<PackageAssociation> associationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PackageObservation>> ListByRepositoryAsync(
        Id<Repository> repositoryId, CancellationToken cancellationToken);
}
