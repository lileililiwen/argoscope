using Argoscope.Application.Analytics;
using Argoscope.Application.Benchmarks;
using Argoscope.Application.Collection;
using Argoscope.Application.Engagement;
using Argoscope.Application.Metrics;
using Argoscope.Application.Portfolios;
using Argoscope.Application.Ranking;
using Argoscope.Domain.Common;
using Argoscope.Domain.Engagement;
using Argoscope.Domain.Memberships;
using Argoscope.Domain.Portfolios;
using Argoscope.Domain.Repositories;
using Argoscope.Domain.Scores;
using Argoscope.Domain.Snapshots;
using Microsoft.EntityFrameworkCore;

namespace Argoscope.Infrastructure.Persistence;

public sealed class EfPortfolioRepository : IPortfolioRepository, IWritePortfolioRepository
{
    private readonly ArgoscopeDbContext _db;
    public EfPortfolioRepository(ArgoscopeDbContext db) => _db = db;

    public Task<Portfolio?> FindAsync(Id<Portfolio> portfolioId, CancellationToken cancellationToken) =>
        _db.Portfolios.FirstOrDefaultAsync(p => p.Id == portfolioId, cancellationToken);

    public async Task<IReadOnlyList<Id<Portfolio>>> ListAllAsync(CancellationToken cancellationToken) =>
        await _db.Portfolios.Select(p => p.Id).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task AddAsync(Portfolio portfolio, CancellationToken cancellationToken)
    {
        await _db.Portfolios.AddAsync(portfolio, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class EfRepositoryStore : IRepositoryStore
{
    private readonly ArgoscopeDbContext _db;
    public EfRepositoryStore(ArgoscopeDbContext db) => _db = db;

    public Task<Repository?> FindByNodeIdAsync(string nodeId, CancellationToken cancellationToken) =>
        _db.Repositories.FirstOrDefaultAsync(r => r.NodeId == nodeId, cancellationToken);

    public Task<Repository?> FindByLocatorAsync(string ownerLogin, string name, CancellationToken cancellationToken) =>
        _db.Repositories.FirstOrDefaultAsync(r => r.OwnerLogin == ownerLogin && r.Name == name, cancellationToken);

    public async Task<Repository> AddAsync(Repository repository, CancellationToken cancellationToken)
    {
        await _db.Repositories.AddAsync(repository, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return repository;
    }

    public async Task UpdateLocatorAsync(Repository repository, CancellationToken cancellationToken)
    {
        _db.Repositories.Update(repository);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Repository>> ListByPortfolioAsync(Id<Portfolio> portfolioId, CancellationToken cancellationToken)
    {
        var ids = await _db.Memberships
            .Where(m => m.PortfolioId == portfolioId)
            .Select(m => m.RepositoryId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return await _db.Repositories.Where(r => ids.Contains(r.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<Repository?> FindAsync(Id<Repository> repositoryId, CancellationToken cancellationToken) =>
        _db.Repositories.FirstOrDefaultAsync(r => r.Id == repositoryId, cancellationToken);
}

public sealed class EfMembershipStore : IMembershipStore
{
    private readonly ArgoscopeDbContext _db;
    public EfMembershipStore(ArgoscopeDbContext db) => _db = db;

    public Task<PortfolioRepository?> FindByRepositoryAsync(Id<Portfolio> portfolioId, Id<Repository> repositoryId, CancellationToken cancellationToken) =>
        _db.Memberships.FirstOrDefaultAsync(m => m.PortfolioId == portfolioId && m.RepositoryId == repositoryId, cancellationToken);

    public async Task<PortfolioRepository> AddAsync(PortfolioRepository membership, CancellationToken cancellationToken)
    {
        await _db.Memberships.AddAsync(membership, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return membership;
    }

    public async Task UpdateAsync(PortfolioRepository membership, CancellationToken cancellationToken)
    {
        _db.Memberships.Update(membership);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PortfolioRepository>> ListByPortfolioAsync(Id<Portfolio> portfolioId, CancellationToken cancellationToken) =>
        await _db.Memberships.Where(m => m.PortfolioId == portfolioId).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task RemoveAsync(Id<PortfolioRepository> membershipId, CancellationToken cancellationToken)
    {
        var entity = await _db.Memberships.FirstOrDefaultAsync(m => m.Id == membershipId, cancellationToken).ConfigureAwait(false);
        if (entity is null) return;
        _db.Memberships.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class EfMetricSnapshotStore : IMetricSnapshotStore
{
    private readonly ArgoscopeDbContext _db;
    public EfMetricSnapshotStore(ArgoscopeDbContext db) => _db = db;

    public Task<MetricSnapshot?> FindAsync(Id<Repository> repositoryId, string metricName, DateOnly date, string providerVersion, CancellationToken cancellationToken) =>
        _db.MetricSnapshots.FirstOrDefaultAsync(
            s => s.RepositoryId == repositoryId && s.MetricName == metricName && s.MetricDate == date && s.ProviderVersion == providerVersion,
            cancellationToken);

    public async Task UpsertAsync(MetricSnapshot snapshot, CancellationToken cancellationToken)
    {
        // Find the (repo, metric, date, provider) row. If a verified one already exists, do not overwrite with missing.
        var existing = await FindAsync(snapshot.RepositoryId, snapshot.MetricName, snapshot.MetricDate, snapshot.ProviderVersion, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (!snapshot.IsComplete && existing.IsComplete)
            {
                return; // keep last good
            }
            _db.MetricSnapshots.Remove(existing);
        }
        await _db.MetricSnapshots.AddAsync(snapshot, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MetricSnapshot>> ListByRepositoryAsync(Id<Repository> repositoryId, CancellationToken cancellationToken) =>
        await _db.MetricSnapshots.Where(s => s.RepositoryId == repositoryId).ToListAsync(cancellationToken).ConfigureAwait(false);
}

public sealed class EfEngagementStore : IEngagementStore
{
    private readonly ArgoscopeDbContext _db;
    public EfEngagementStore(ArgoscopeDbContext db) => _db = db;

    public Task<EngagementBucket?> FindAsync(Id<Repository> repositoryId, DateOnly date, CancellationToken cancellationToken) =>
        _db.EngagementBuckets.FirstOrDefaultAsync(e => e.RepositoryId == repositoryId && e.BucketDate == date, cancellationToken);

    public async Task UpsertAsync(EngagementBucket bucket, CancellationToken cancellationToken)
    {
        var existing = await FindAsync(bucket.RepositoryId, bucket.BucketDate, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            _db.EngagementBuckets.Remove(existing);
        }
        await _db.EngagementBuckets.AddAsync(bucket, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<EngagementBucket>> ListByRepositoryAsync(Id<Repository> repositoryId, CancellationToken cancellationToken) =>
        await _db.EngagementBuckets.Where(e => e.RepositoryId == repositoryId).ToListAsync(cancellationToken).ConfigureAwait(false);
}

public sealed class EfCheckpointStore : ICheckpointStore
{
    private readonly ArgoscopeDbContext _db;
    public EfCheckpointStore(ArgoscopeDbContext db) => _db = db;

    public Task<CollectionCheckpoint?> FindAsync(Id<Portfolio> portfolioId, Id<Repository> repositoryId, string providerVersion, CancellationToken cancellationToken) =>
        _db.CollectionCheckpoints.FirstOrDefaultAsync(c => c.PortfolioId == portfolioId && c.RepositoryId == repositoryId && c.ProviderVersion == providerVersion, cancellationToken);

    public async Task<CollectionCheckpoint> GetOrCreateAsync(Id<Portfolio> portfolioId, Id<Repository> repositoryId, string providerVersion, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await FindAsync(portfolioId, repositoryId, providerVersion, cancellationToken).ConfigureAwait(false);
        if (existing is not null) return existing;
        var created = new CollectionCheckpoint(portfolioId, repositoryId, providerVersion, now);
        await _db.CollectionCheckpoints.AddAsync(created, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return created;
    }

    public async Task UpdateAsync(CollectionCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        _db.CollectionCheckpoints.Update(checkpoint);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class EfScoreConfigurationStore : IScoreConfigurationStore
{
    private readonly ArgoscopeDbContext _db;
    public EfScoreConfigurationStore(ArgoscopeDbContext db) => _db = db;

    public async Task<ScoreConfiguration?> GetActiveAsync(Id<Portfolio> portfolioId, CancellationToken cancellationToken) =>
        await _db.ScoreConfigurations
            .Where(c => c.PortfolioId == portfolioId)
            .OrderByDescending(c => c.Version)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task UpsertActiveAsync(ScoreConfiguration configuration, CancellationToken cancellationToken)
    {
        await _db.ScoreConfigurations.AddAsync(configuration, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
