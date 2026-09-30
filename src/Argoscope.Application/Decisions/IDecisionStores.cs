using Argoscope.Domain.Common;
using Argoscope.Domain.Decisions;
using Argoscope.Domain.Portfolios;
using Argoscope.Domain.Repositories;

namespace Argoscope.Application.Decisions;

/// <summary>
/// Persistence contract for the decision-aggregate root. The
/// infrastructure layer implements this with EF Core; tests implement
/// it with in-memory stores. The store preserves soft-deleted rows
/// and never silently drops history.
/// </summary>
public interface IDecisionEntryStore
{
    Task<DecisionEntry?> FindAsync(Id<DecisionEntry> id, CancellationToken cancellationToken);

    Task<DecisionEntry?> FindByIdempotencyKeyAsync(
        Id<Portfolio> portfolioId, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>
    /// List decisions for a portfolio. When <paramref name="includeDeleted"/> is
    /// <c>false</c> soft-deleted rows are hidden (the default for the public
    /// timeline). Pass <c>true</c> to surface the trash bin.
    /// </summary>
    Task<IReadOnlyList<DecisionEntry>> ListByPortfolioAsync(
        Id<Portfolio> portfolioId, bool includeDeleted, CancellationToken cancellationToken);

    /// <summary>
    /// List decisions attached to a specific repository, ordered by
    /// decision date then created time. Honours the same soft-delete
    /// semantics as <see cref="ListByPortfolioAsync"/>.
    /// </summary>
    Task<IReadOnlyList<DecisionEntry>> ListByRepositoryAsync(
        Id<Repository> repositoryId, bool includeDeleted, CancellationToken cancellationToken);

    Task<DecisionEntry> AddAsync(DecisionEntry entry, CancellationToken cancellationToken);

    Task UpdateAsync(DecisionEntry entry, CancellationToken cancellationToken);
}

public interface IDecisionRevisionStore
{
    /// <summary>Append a new revision. The store is the only writer of revisions.</summary>
    Task<DecisionRevision> AddAsync(DecisionRevision revision, CancellationToken cancellationToken);

    /// <summary>List revisions for a single decision, ordered by revision number ascending.</summary>
    Task<IReadOnlyList<DecisionRevision>> ListByEntryAsync(
        Id<DecisionEntry> decisionEntryId, CancellationToken cancellationToken);

    /// <summary>
    /// Load the most recent revision for a decision. Used to compute the
    /// evidence manifest that the read API returns.
    /// </summary>
    Task<DecisionRevision?> GetLatestAsync(
        Id<DecisionEntry> decisionEntryId, CancellationToken cancellationToken);
}

public interface IDecisionEvidenceStore
{
    /// <summary>Add evidence references for a revision in one batch.</summary>
    Task AddRangeAsync(
        IReadOnlyList<DecisionEvidenceReference> references, CancellationToken cancellationToken);

    /// <summary>List evidence references for a revision, ordered by id (stable).</summary>
    Task<IReadOnlyList<DecisionEvidenceReference>> ListByRevisionAsync(
        Id<DecisionRevision> revisionId, CancellationToken cancellationToken);

    /// <summary>List evidence references for a list of revisions in one call.</summary>
    Task<IReadOnlyDictionary<Id<DecisionRevision>, IReadOnlyList<DecisionEvidenceReference>>> ListByRevisionsAsync(
        IReadOnlyList<Id<DecisionRevision>> revisionIds, CancellationToken cancellationToken);
}
