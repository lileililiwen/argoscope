using Argoscope.Domain.Common;
using Argoscope.Domain.Portfolios;
using Argoscope.Domain.Repositories;

namespace Argoscope.Domain.Snapshots;

/// <summary>
/// Resumable per-(portfolio, repository) collection state. Holds the cursor
/// for the next provider call and the last failure classification; never
/// contains the GitHub token.
/// </summary>
public sealed class CollectionCheckpoint : Entity<Id<CollectionCheckpoint>>
{
    public Id<Portfolio> PortfolioId { get; private set; }

    public Id<Repository> RepositoryId { get; private set; }

    public string ProviderVersion { get; private set; }

    public string? Cursor { get; private set; }

    public DateTimeOffset? LastSuccessAtUtc { get; private set; }

    public DateTimeOffset? LastAttemptAtUtc { get; private set; }

    public ProviderResultStatus LastStatus { get; private set; }

    public DateTimeOffset? RetryAfterUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private CollectionCheckpoint() : base() { }

    public CollectionCheckpoint(
        Id<Portfolio> portfolioId,
        Id<Repository> repositoryId,
        string providerVersion,
        DateTimeOffset now)
        : base(Id<CollectionCheckpoint>.New())
    {
        PortfolioId = portfolioId;
        RepositoryId = repositoryId;
        ProviderVersion = providerVersion.Trim();
        LastStatus = ProviderResultStatus.Unavailable;
        UpdatedAtUtc = now;
    }

    public void RecordSuccess(string? cursor, DateTimeOffset now)
    {
        Cursor = cursor;
        LastSuccessAtUtc = now;
        LastAttemptAtUtc = now;
        LastStatus = ProviderResultStatus.Available;
        RetryAfterUtc = null;
        UpdatedAtUtc = now;
    }

    public void RecordPartial(string? cursor, DateTimeOffset now)
    {
        Cursor = cursor;
        LastAttemptAtUtc = now;
        LastStatus = ProviderResultStatus.Partial;
        RetryAfterUtc = null;
        UpdatedAtUtc = now;
    }

    public void RecordFailure(ProviderResultStatus status, string? cursor, DateTimeOffset? retryAfterUtc, DateTimeOffset now)
    {
        Cursor = cursor;
        LastAttemptAtUtc = now;
        LastStatus = status;
        RetryAfterUtc = retryAfterUtc;
        UpdatedAtUtc = now;
    }
}
