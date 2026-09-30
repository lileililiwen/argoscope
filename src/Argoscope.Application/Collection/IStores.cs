using Argoscope.Domain.Common;
using Argoscope.Domain.Memberships;
using Argoscope.Domain.Portfolios;
using Argoscope.Domain.Repositories;
using Argoscope.Domain.Snapshots;
using Argoscope.GitHub;

namespace Argoscope.Application.Collection;

/// <summary>Inputs for one collection run.</summary>
public sealed record CollectionRequest(
    Id<Portfolio> PortfolioId,
    string OwnerLogin,
    string Name,
    DateOnly MetricDate,
    DateTimeOffset AsOfUtc);

/// <summary>Result of one collection run.</summary>
public sealed record CollectionRunResult(
    Id<Repository> RepositoryId,
    int SnapshotsWritten,
    int EngagementBucketsWritten,
    ProviderResultStatus RepositoryStatus,
    ProviderResultStatus MetricsStatus,
    ProviderResultStatus EngagementStatus,
    DateTimeOffset RunAtUtc);

/// <summary>
/// Read-side contracts the collection service depends on. The infrastructure
/// layer implements these with EF Core; tests implement them with in-memory
/// stores. Keeping the contracts narrow makes the collection service easy to
/// unit-test against deterministic providers.
/// </summary>
public interface IPortfolioRepository
{
    Task<Portfolio?> FindAsync(Id<Portfolio> portfolioId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Id<Portfolio>>> ListAllAsync(CancellationToken cancellationToken);
}

public interface IRepositoryStore
{
    Task<Repository?> FindByNodeIdAsync(string nodeId, CancellationToken cancellationToken);
    Task<Repository?> FindByLocatorAsync(string ownerLogin, string name, CancellationToken cancellationToken);
    Task<Repository> AddAsync(Repository repository, CancellationToken cancellationToken);
    Task UpdateLocatorAsync(Repository repository, CancellationToken cancellationToken);
    Task<IReadOnlyList<Repository>> ListByPortfolioAsync(Id<Portfolio> portfolioId, CancellationToken cancellationToken);
    Task<Repository?> FindAsync(Id<Repository> repositoryId, CancellationToken cancellationToken);
}

public interface IMembershipStore
{
    Task<PortfolioRepository?> FindByRepositoryAsync(Id<Portfolio> portfolioId, Id<Repository> repositoryId, CancellationToken cancellationToken);
    Task<PortfolioRepository> AddAsync(PortfolioRepository membership, CancellationToken cancellationToken);
    Task UpdateAsync(PortfolioRepository membership, CancellationToken cancellationToken);
    Task<IReadOnlyList<PortfolioRepository>> ListByPortfolioAsync(Id<Portfolio> portfolioId, CancellationToken cancellationToken);
    Task RemoveAsync(Id<PortfolioRepository> membershipId, CancellationToken cancellationToken);
}

public interface IMetricSnapshotStore
{
    Task<MetricSnapshot?> FindByIdAsync(Id<MetricSnapshot> id, CancellationToken cancellationToken);
    Task<MetricSnapshot?> FindAsync(Id<Repository> repositoryId, string metricName, DateOnly date, string providerVersion, CancellationToken cancellationToken);
    Task UpsertAsync(MetricSnapshot snapshot, CancellationToken cancellationToken);
    Task<IReadOnlyList<MetricSnapshot>> ListByRepositoryAsync(Id<Repository> repositoryId, CancellationToken cancellationToken);
}

public interface IEngagementStore
{
    Task<Domain.Engagement.EngagementBucket?> FindAsync(Id<Repository> repositoryId, DateOnly date, CancellationToken cancellationToken);
    Task UpsertAsync(Domain.Engagement.EngagementBucket bucket, CancellationToken cancellationToken);
    Task<IReadOnlyList<Domain.Engagement.EngagementBucket>> ListByRepositoryAsync(Id<Repository> repositoryId, CancellationToken cancellationToken);
}

public interface ICheckpointStore
{
    Task<CollectionCheckpoint?> FindAsync(Id<Portfolio> portfolioId, Id<Repository> repositoryId, string providerVersion, CancellationToken cancellationToken);
    Task<CollectionCheckpoint> GetOrCreateAsync(Id<Portfolio> portfolioId, Id<Repository> repositoryId, string providerVersion, DateTimeOffset now, CancellationToken cancellationToken);
    Task UpdateAsync(CollectionCheckpoint checkpoint, CancellationToken cancellationToken);
}
