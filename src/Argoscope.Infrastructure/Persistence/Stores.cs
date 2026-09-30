using Argoscope.Application.Analytics;
using Argoscope.Application.Benchmarks;
using Argoscope.Application.Collection;
using Argoscope.Application.Decisions;
using Argoscope.Application.Engagement;
using Argoscope.Application.Metrics;
using Argoscope.Application.Packages;
using Argoscope.Application.Portfolios;
using Argoscope.Application.Ranking;
using Argoscope.Domain.Common;
using Argoscope.Domain.Decisions;
using Argoscope.Domain.Engagement;
using Argoscope.Domain.Memberships;
using Argoscope.Domain.Packages;
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

    public Task<MetricSnapshot?> FindByIdAsync(Id<MetricSnapshot> id, CancellationToken cancellationToken) =>
        _db.MetricSnapshots.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

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

public sealed class EfPackageAssociationStore : IPackageAssociationStore
{
    private readonly ArgoscopeDbContext _db;
    public EfPackageAssociationStore(ArgoscopeDbContext db) => _db = db;

    public Task<PackageAssociation?> FindAsync(Id<PackageAssociation> id, CancellationToken cancellationToken) =>
        _db.PackageAssociations.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<PackageAssociation?> FindByRepositoryAndCoordinateAsync(
        Id<Repository> repositoryId, PackageProvider provider, string coordinate, CancellationToken cancellationToken) =>
        _db.PackageAssociations.FirstOrDefaultAsync(
            p => p.RepositoryId == repositoryId && p.Provider == provider && p.Coordinate == coordinate,
            cancellationToken);

    public async Task<IReadOnlyList<PackageAssociation>> ListByRepositoryAsync(
        Id<Repository> repositoryId, CancellationToken cancellationToken) =>
        await _db.PackageAssociations
            .Where(p => p.RepositoryId == repositoryId)
            .OrderBy(p => p.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<PackageAssociation> AddAsync(PackageAssociation association, CancellationToken cancellationToken)
    {
        await _db.PackageAssociations.AddAsync(association, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return association;
    }

    public async Task UpdateAsync(PackageAssociation association, CancellationToken cancellationToken)
    {
        _db.PackageAssociations.Update(association);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Id<PackageAssociation> id, CancellationToken cancellationToken)
    {
        var entity = await _db.PackageAssociations.FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);
        if (entity is null) return;
        _db.PackageAssociations.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class EfPackageObservationStore : IPackageObservationStore
{
    private readonly ArgoscopeDbContext _db;
    public EfPackageObservationStore(ArgoscopeDbContext db) => _db = db;

    public Task<PackageObservation?> FindByIdAsync(Id<PackageObservation> id, CancellationToken cancellationToken) =>
        _db.PackageObservations.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<PackageObservation?> FindAsync(
        Id<PackageAssociation> associationId,
        PackageUnit unit,
        PackageWindow window,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc,
        CancellationToken cancellationToken) =>
        _db.PackageObservations.FirstOrDefaultAsync(
            p => p.PackageAssociationId == associationId
                && p.Unit == unit
                && p.Window == window
                && p.WindowStartUtc == windowStartUtc
                && p.WindowEndUtc == windowEndUtc,
            cancellationToken);

    public async Task<PackageObservation> UpsertAsync(PackageObservation observation, CancellationToken cancellationToken)
    {
        var existing = await FindAsync(
            observation.PackageAssociationId, observation.Unit, observation.Window,
            observation.WindowStartUtc, observation.WindowEndUtc, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            // Last-good: do not overwrite a previously stored complete
            // observation with a partial one. A new complete observation
            // replaces the prior one.
            if (existing.IsComplete && !observation.IsComplete)
            {
                return existing;
            }
            _db.PackageObservations.Remove(existing);
        }
        await _db.PackageObservations.AddAsync(observation, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return observation;
    }

    public async Task<IReadOnlyList<PackageObservation>> ListByAssociationAsync(
        Id<PackageAssociation> associationId, CancellationToken cancellationToken) =>
        await _db.PackageObservations
            .Where(p => p.PackageAssociationId == associationId)
            .OrderBy(p => p.WindowStartUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<PackageObservation>> ListByRepositoryAsync(
        Id<Repository> repositoryId, CancellationToken cancellationToken)
    {
        var associationIds = await _db.PackageAssociations
            .Where(p => p.RepositoryId == repositoryId)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (associationIds.Count == 0)
        {
            return Array.Empty<PackageObservation>();
        }
        return await _db.PackageObservations
            .Where(p => associationIds.Contains(p.PackageAssociationId))
            .OrderBy(p => p.WindowStartUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}

public sealed class EfDecisionEntryStore : IDecisionEntryStore
{
    private readonly ArgoscopeDbContext _db;
    public EfDecisionEntryStore(ArgoscopeDbContext db) => _db = db;

    public Task<DecisionEntry?> FindAsync(Id<DecisionEntry> id, CancellationToken cancellationToken) =>
        _db.DecisionEntries.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public Task<DecisionEntry?> FindByIdempotencyKeyAsync(
        Id<Portfolio> portfolioId, string idempotencyKey, CancellationToken cancellationToken) =>
        _db.DecisionEntries.FirstOrDefaultAsync(
            d => d.PortfolioId == portfolioId && d.IdempotencyKey == idempotencyKey,
            cancellationToken);

    public async Task<IReadOnlyList<DecisionEntry>> ListByPortfolioAsync(
        Id<Portfolio> portfolioId, bool includeDeleted, CancellationToken cancellationToken)
    {
        var query = _db.DecisionEntries
            .Where(d => d.PortfolioId == portfolioId);
        if (!includeDeleted)
        {
            query = query.Where(d => d.DeletedAtUtc == null);
        }
        return await query
            .OrderByDescending(d => d.DecisionDate)
            .ThenByDescending(d => d.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DecisionEntry>> ListByRepositoryAsync(
        Id<Repository> repositoryId, bool includeDeleted, CancellationToken cancellationToken)
    {
        var query = _db.DecisionEntries
            .Where(d => d.RepositoryId == repositoryId);
        if (!includeDeleted)
        {
            query = query.Where(d => d.DeletedAtUtc == null);
        }
        return await query
            .OrderByDescending(d => d.DecisionDate)
            .ThenByDescending(d => d.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<DecisionEntry> AddAsync(DecisionEntry entry, CancellationToken cancellationToken)
    {
        await _db.DecisionEntries.AddAsync(entry, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return entry;
    }

    public async Task UpdateAsync(DecisionEntry entry, CancellationToken cancellationToken)
    {
        _db.DecisionEntries.Update(entry);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class EfDecisionRevisionStore : IDecisionRevisionStore
{
    private readonly ArgoscopeDbContext _db;
    public EfDecisionRevisionStore(ArgoscopeDbContext db) => _db = db;

    public async Task<DecisionRevision> AddAsync(DecisionRevision revision, CancellationToken cancellationToken)
    {
        await _db.DecisionRevisions.AddAsync(revision, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return revision;
    }

    public async Task<IReadOnlyList<DecisionRevision>> ListByEntryAsync(
        Id<DecisionEntry> decisionEntryId, CancellationToken cancellationToken) =>
        await _db.DecisionRevisions
            .Where(r => r.DecisionEntryId == decisionEntryId)
            .OrderBy(r => r.RevisionNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<DecisionRevision?> GetLatestAsync(
        Id<DecisionEntry> decisionEntryId, CancellationToken cancellationToken) =>
        _db.DecisionRevisions
            .Where(r => r.DecisionEntryId == decisionEntryId)
            .OrderByDescending(r => r.RevisionNumber)
            .FirstOrDefaultAsync(cancellationToken);
}

public sealed class EfDecisionEvidenceStore : IDecisionEvidenceStore
{
    private readonly ArgoscopeDbContext _db;
    public EfDecisionEvidenceStore(ArgoscopeDbContext db) => _db = db;

    public async Task AddRangeAsync(
        IReadOnlyList<DecisionEvidenceReference> references, CancellationToken cancellationToken)
    {
        if (references.Count == 0) return;
        await _db.DecisionEvidenceReferences.AddRangeAsync(references, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DecisionEvidenceReference>> ListByRevisionAsync(
        Id<DecisionRevision> revisionId, CancellationToken cancellationToken) =>
        await _db.DecisionEvidenceReferences
            .Where(r => r.DecisionRevisionId == revisionId)
            .OrderBy(r => r.Id.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyDictionary<Id<DecisionRevision>, IReadOnlyList<DecisionEvidenceReference>>> ListByRevisionsAsync(
        IReadOnlyList<Id<DecisionRevision>> revisionIds, CancellationToken cancellationToken)
    {
        if (revisionIds.Count == 0)
        {
            return new Dictionary<Id<DecisionRevision>, IReadOnlyList<DecisionEvidenceReference>>();
        }
        var rows = await _db.DecisionEvidenceReferences
            .Where(r => revisionIds.Contains(r.DecisionRevisionId))
            .OrderBy(r => r.Id.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var grouped = rows
            .GroupBy(r => r.DecisionRevisionId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<DecisionEvidenceReference>)g.ToList());
        return grouped;
    }
}
