using Argoscope.Application.Collection;
using Argoscope.Application.Decisions;
using Argoscope.Application.Packages;
using Argoscope.Application.Portfolios;
using Argoscope.Domain.Common;
using Argoscope.Domain.Memberships;
using Argoscope.Domain.Packages;
using Argoscope.Domain.Portfolios;
using Argoscope.Domain.Repositories;
using Argoscope.Domain.Snapshots;

namespace Argoscope.UnitTests;

/// <summary>Deterministic clock for tests. Reuses the same <c>UtcNow</c> across an entire test.</summary>
public sealed class FixedClock : IClock
{
    public FixedClock(DateTimeOffset now) => UtcNow = now;
    public DateTimeOffset UtcNow { get; }
}

/// <summary>Trivial portfolio repository for unit tests.</summary>
public sealed class InMemoryPortfolioRepository : IPortfolioRepository
{
    private readonly Dictionary<Id<Portfolio>, Portfolio> _byId = new();

    public void Add(Portfolio p) => _byId[p.Id] = p;

    public Task<Portfolio?> FindAsync(Id<Portfolio> portfolioId, CancellationToken cancellationToken) =>
        Task.FromResult(_byId.TryGetValue(portfolioId, out var v) ? v : null);

    public Task<IReadOnlyList<Id<Portfolio>>> ListAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Id<Portfolio>>>(_byId.Keys.ToList());
}

/// <summary>Stub repository store for unit tests that need to look up a repository by id.</summary>
public sealed class InMemoryRepositoryStore : IRepositoryStore
{
    private readonly Dictionary<Id<Repository>, Repository> _byId = new();
    private readonly InMemoryMembershipStore _memberships;

    public InMemoryRepositoryStore(InMemoryMembershipStore memberships)
    {
        _memberships = memberships;
    }

    public void Add(Repository r) => _byId[r.Id] = r;

    public Task<Repository?> FindByNodeIdAsync(string nodeId, CancellationToken cancellationToken) =>
        Task.FromResult(_byId.Values.FirstOrDefault(r => r.NodeId == nodeId));

    public Task<Repository?> FindByLocatorAsync(string ownerLogin, string name, CancellationToken cancellationToken) =>
        Task.FromResult(_byId.Values.FirstOrDefault(r => r.OwnerLogin == ownerLogin && r.Name == name));

    public Task<Repository> AddAsync(Repository repository, CancellationToken cancellationToken)
    {
        _byId[repository.Id] = repository;
        return Task.FromResult(repository);
    }

    public Task UpdateLocatorAsync(Repository repository, CancellationToken cancellationToken)
    {
        _byId[repository.Id] = repository;
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Repository>> ListByPortfolioAsync(Id<Portfolio> portfolioId, CancellationToken cancellationToken)
    {
        var ids = (await _memberships
            .ListByPortfolioAsync(portfolioId, cancellationToken))
            .Select(m => m.RepositoryId)
            .ToHashSet();
        return _byId.Values.Where(r => ids.Contains(r.Id)).ToList();
    }

    public Task<Repository?> FindAsync(Id<Repository> repositoryId, CancellationToken cancellationToken) =>
        Task.FromResult(_byId.TryGetValue(repositoryId, out var v) ? v : null);
}

/// <summary>Stub membership store for unit tests.</summary>
public sealed class InMemoryMembershipStore : IMembershipStore
{
    private readonly List<PortfolioRepository> _items = new();

    public void Add(PortfolioRepository m) => _items.Add(m);

    public Task<PortfolioRepository?> FindByRepositoryAsync(
        Id<Portfolio> portfolioId, Id<Repository> repositoryId, CancellationToken cancellationToken) =>
        Task.FromResult(_items.FirstOrDefault(m => m.PortfolioId == portfolioId && m.RepositoryId == repositoryId));

    public Task<PortfolioRepository> AddAsync(PortfolioRepository membership, CancellationToken cancellationToken)
    {
        _items.Add(membership);
        return Task.FromResult(membership);
    }

    public Task UpdateAsync(PortfolioRepository membership, CancellationToken cancellationToken)
    {
        var i = _items.FindIndex(m => m.Id == membership.Id);
        if (i >= 0) _items[i] = membership;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PortfolioRepository>> ListByPortfolioAsync(
        Id<Portfolio> portfolioId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PortfolioRepository>>(
            _items.Where(m => m.PortfolioId == portfolioId).ToList());

    public Task RemoveAsync(Id<PortfolioRepository> membershipId, CancellationToken cancellationToken)
    {
        _items.RemoveAll(m => m.Id == membershipId);
        return Task.CompletedTask;
    }
}

/// <summary>Stub metric snapshot store for unit tests.</summary>
public sealed class InMemoryMetricSnapshotStore : IMetricSnapshotStore
{
    private readonly Dictionary<Id<MetricSnapshot>, MetricSnapshot> _byId = new();

    public void Add(MetricSnapshot s) => _byId[s.Id] = s;

    public Task<MetricSnapshot?> FindByIdAsync(Id<MetricSnapshot> id, CancellationToken cancellationToken) =>
        Task.FromResult(_byId.TryGetValue(id, out var v) ? v : null);

    public Task<MetricSnapshot?> FindAsync(
        Id<Repository> repositoryId, string metricName, DateOnly date, string providerVersion, CancellationToken cancellationToken) =>
        Task.FromResult(_byId.Values.FirstOrDefault(s =>
            s.RepositoryId == repositoryId && s.MetricName == metricName
            && s.MetricDate == date && s.ProviderVersion == providerVersion));

    public Task UpsertAsync(MetricSnapshot snapshot, CancellationToken cancellationToken)
    {
        _byId[snapshot.Id] = snapshot;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MetricSnapshot>> ListByRepositoryAsync(Id<Repository> repositoryId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MetricSnapshot>>(_byId.Values.Where(s => s.RepositoryId == repositoryId).ToList());
}
