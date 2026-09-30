using Argoscope.Domain.Common;
using Argoscope.Domain.Portfolios;
using Argoscope.Domain.Repositories;

namespace Argoscope.Domain.Decisions;

/// <summary>
/// Owner-authored portfolio or repository decision. The aggregate root
/// owns its append-only <see cref="DecisionRevision"/> history; the
/// current state is the latest revision's after-image and the entry
/// itself only carries the current <see cref="RevisionNumber"/>,
/// identity, soft-delete tombstone, and a nullable
/// <see cref="IdempotencyKey"/> so retried POSTs converge on the
/// original row.
/// </summary>
public sealed class DecisionEntry : Entity<Id<DecisionEntry>>
{
    public const int MaxRationaleLength = 10_000;
    public const int MaxIdempotencyKeyLength = 200;

    public Id<Portfolio> PortfolioId { get; private set; }

    public Id<Repository>? RepositoryId { get; private set; }

    public DecisionType DecisionType { get; private set; }

    public DateOnly DecisionDate { get; private set; }

    public string Rationale { get; private set; }

    public DateOnly? ReviewDate { get; private set; }

    /// <summary>
    /// Monotonic revision number, incremented on every successful
    /// mutation. The first revision is 1. Used for optimistic
    /// concurrency and to anchor <c>expectedRevision</c> in API
    /// requests.
    /// </summary>
    public int RevisionNumber { get; private set; }

    /// <summary>
    /// Optional client-supplied idempotency key. When set, retried
    /// POSTs with the same key against the same portfolio return the
    /// existing row instead of creating a duplicate.
    /// </summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>Soft-delete tombstone. <c>null</c> while the entry is live.</summary>
    public DateTimeOffset? DeletedAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private DecisionEntry() : base() { }

    public DecisionEntry(
        Id<Portfolio> portfolioId,
        Id<Repository>? repositoryId,
        DecisionType decisionType,
        DateOnly decisionDate,
        string rationale,
        DateOnly? reviewDate,
        string? idempotencyKey,
        DateTimeOffset now)
        : base(Id<DecisionEntry>.New())
    {
        ValidateRationale(rationale);
        ValidateIdempotencyKey(idempotencyKey);
        ValidateReviewDate(decisionDate, reviewDate);

        PortfolioId = portfolioId;
        RepositoryId = repositoryId;
        DecisionType = decisionType;
        DecisionDate = decisionDate;
        Rationale = rationale.Trim();
        ReviewDate = reviewDate;
        IdempotencyKey = NormalizeIdempotencyKey(idempotencyKey);
        RevisionNumber = 1;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
    }

    /// <summary>
    /// Apply a new revision. Updates the current fields, increments
    /// the revision number, and resets the soft-delete tombstone. The
    /// call does NOT itself append a <see cref="DecisionRevision"/>
    /// row; the application layer is responsible for persisting the
    /// before/after audit entry alongside the mutation.
    /// </summary>
    public void ApplyUpdate(
        DecisionType decisionType,
        DateOnly decisionDate,
        string rationale,
        DateOnly? reviewDate,
        DateTimeOffset now)
    {
        if (DeletedAtUtc is not null)
        {
            throw new DomainException("conflict", "Decision has been deleted; restore it before editing.");
        }
        ValidateRationale(rationale);
        ValidateReviewDate(decisionDate, reviewDate);

        DecisionType = decisionType;
        DecisionDate = decisionDate;
        Rationale = rationale.Trim();
        ReviewDate = reviewDate;
        RevisionNumber += 1;
        UpdatedAtUtc = now;
    }

    /// <summary>Mark the entry as soft-deleted. The history remains readable.</summary>
    public void MarkDeleted(DateTimeOffset now)
    {
        if (DeletedAtUtc is not null)
        {
            throw new DomainException("conflict", "Decision is already deleted.");
        }
        DeletedAtUtc = now;
        RevisionNumber += 1;
        UpdatedAtUtc = now;
    }

    /// <summary>Restore a previously soft-deleted entry.</summary>
    public void MarkRestored(DateTimeOffset now)
    {
        if (DeletedAtUtc is null)
        {
            throw new DomainException("conflict", "Decision is not deleted.");
        }
        DeletedAtUtc = null;
        RevisionNumber += 1;
        UpdatedAtUtc = now;
    }

    /// <summary>True when the entry is currently soft-deleted.</summary>
    public bool IsDeleted => DeletedAtUtc is not null;

    private static void ValidateRationale(string rationale)
    {
        if (string.IsNullOrWhiteSpace(rationale))
        {
            throw new DomainException("validation", "Rationale is required.");
        }
        if (rationale.Length > MaxRationaleLength)
        {
            throw new DomainException(
                "validation",
                $"Rationale must be {MaxRationaleLength} characters or fewer.");
        }
    }

    private static void ValidateIdempotencyKey(string? key)
    {
        if (key is null) return;
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new DomainException("validation", "Idempotency key must not be empty when provided.");
        }
        if (key.Length > MaxIdempotencyKeyLength)
        {
            throw new DomainException(
                "validation",
                $"Idempotency key must be {MaxIdempotencyKeyLength} characters or fewer.");
        }
    }

    private static void ValidateReviewDate(DateOnly decisionDate, DateOnly? reviewDate)
    {
        if (reviewDate is null) return;
        if (reviewDate.Value < decisionDate)
        {
            throw new DomainException(
                "validation",
                "Review date must be on or after the decision date.");
        }
    }

    private static string? NormalizeIdempotencyKey(string? key) =>
        key is null ? null : key.Trim();
}
