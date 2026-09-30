using Argoscope.Application.Packages;
using Argoscope.Domain.Common;
using Argoscope.Domain.Packages;
using Argoscope.Domain.Repositories;

namespace Argoscope.UnitTests;

/// <summary>Thread-unsafe in-memory test double for IPackageAssociationStore.</summary>
public sealed class InMemoryPackageAssociationStore : IPackageAssociationStore
{
    private readonly Dictionary<Id<PackageAssociation>, PackageAssociation> _byId = new();
    private readonly object _lock = new();

    public Task<PackageAssociation?> FindAsync(Id<PackageAssociation> id, CancellationToken cancellationToken)
    {
        lock (_lock) { return Task.FromResult(_byId.TryGetValue(id, out var v) ? v : null); }
    }

    public Task<PackageAssociation?> FindByRepositoryAndCoordinateAsync(
        Id<Repository> repositoryId, PackageProvider provider, string coordinate, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult(_byId.Values.FirstOrDefault(a =>
                a.RepositoryId == repositoryId && a.Provider == provider && a.Coordinate == coordinate));
        }
    }

    public Task<IReadOnlyList<PackageAssociation>> ListByRepositoryAsync(Id<Repository> repositoryId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<PackageAssociation>>(
                _byId.Values.Where(a => a.RepositoryId == repositoryId).OrderBy(a => a.CreatedAtUtc).ToList());
        }
    }

    public Task<PackageAssociation> AddAsync(PackageAssociation association, CancellationToken cancellationToken)
    {
        lock (_lock) { _byId[association.Id] = association; }
        return Task.FromResult(association);
    }

    public Task UpdateAsync(PackageAssociation association, CancellationToken cancellationToken)
    {
        lock (_lock) { _byId[association.Id] = association; }
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Id<PackageAssociation> id, CancellationToken cancellationToken)
    {
        lock (_lock) { _byId.Remove(id); }
        return Task.CompletedTask;
    }
}

/// <summary>Thread-unsafe in-memory test double for IPackageObservationStore.</summary>
public sealed class InMemoryPackageObservationStore : IPackageObservationStore
{
    private readonly List<PackageObservation> _items = new();
    private readonly object _lock = new();

    public Task<PackageObservation?> FindAsync(
        Id<PackageAssociation> associationId,
        PackageUnit unit,
        PackageWindow window,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc,
        CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult(_items.FirstOrDefault(o =>
                o.PackageAssociationId == associationId
                && o.Unit == unit
                && o.Window == window
                && o.WindowStartUtc == windowStartUtc
                && o.WindowEndUtc == windowEndUtc));
        }
    }

    public Task<PackageObservation?> FindByIdAsync(Id<PackageObservation> id, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult(_items.FirstOrDefault(o => o.Id == id));
        }
    }

    public Task<PackageObservation> UpsertAsync(PackageObservation observation, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var existing = _items.FirstOrDefault(o =>
                o.PackageAssociationId == observation.PackageAssociationId
                && o.Unit == observation.Unit
                && o.Window == observation.Window
                && o.WindowStartUtc == observation.WindowStartUtc
                && o.WindowEndUtc == observation.WindowEndUtc);
            if (existing is not null)
            {
                if (existing.IsComplete && !observation.IsComplete)
                {
                    return Task.FromResult(existing);
                }
                _items.Remove(existing);
            }
            _items.Add(observation);
            return Task.FromResult(observation);
        }
    }

    public Task<IReadOnlyList<PackageObservation>> ListByAssociationAsync(
        Id<PackageAssociation> associationId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<PackageObservation>>(
                _items.Where(o => o.PackageAssociationId == associationId).OrderBy(o => o.WindowStartUtc).ToList());
        }
    }

    public Task<IReadOnlyList<PackageObservation>> ListByRepositoryAsync(
        Id<Repository> repositoryId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<PackageObservation>>(
                _items.OrderBy(o => o.WindowStartUtc).ToList());
        }
    }
}
