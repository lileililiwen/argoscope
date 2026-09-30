using Argoscope.Application.Collection;
using Argoscope.Domain.Common;
using Argoscope.Domain.Repositories;

namespace Argoscope.Application.Packages;

/// <summary>
/// Read-side service that builds the per-repository adoption report.
/// Pure with respect to the supplied stores.
/// </summary>
public sealed class PackageAdoptionService
{
    private readonly IPackageAssociationStore _associations;
    private readonly IPackageObservationStore _observations;
    private readonly IRepositoryStore _repositories;
    private readonly IClock _clock;

    public PackageAdoptionService(
        IPackageAssociationStore associations,
        IPackageObservationStore observations,
        IRepositoryStore repositories,
        IClock clock)
    {
        _associations = associations;
        _observations = observations;
        _repositories = repositories;
        _clock = clock;
    }

    public async Task<PackageAdoptionReport?> BuildForRepositoryAsync(
        Id<Repository> repositoryId,
        CancellationToken cancellationToken)
    {
        var repo = await _repositories.FindAsync(repositoryId, cancellationToken).ConfigureAwait(false);
        if (repo is null) return null;
        var associations = await _associations.ListByRepositoryAsync(repositoryId, cancellationToken).ConfigureAwait(false);
        var observations = await _observations.ListByRepositoryAsync(repositoryId, cancellationToken).ConfigureAwait(false);
        return PackageAdoptionAggregator.Build(repositoryId, associations, observations, _clock.UtcNow);
    }
}
