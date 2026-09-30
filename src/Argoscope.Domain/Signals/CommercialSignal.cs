using Argoscope.Domain.Common;
using Argoscope.Domain.Repositories;

namespace Argoscope.Domain.Signals;

/// <summary>
/// One versioned commercial-signal suggestion for a single issue/PR content
/// version. A changed title/body hash creates a new row with an incremented
/// <see cref="SuggestionVersion"/>; human decisions on older rows are never
/// overwritten. <see cref="Version"/> is the optimistic-concurrency token for
/// reviews. No property here affects scores, ranks, lifecycle or GitHub.
/// </summary>
public sealed class CommercialSignal : Entity<Id<CommercialSignal>>
{
    public const int MaxExcerptLength = 4000;
    public const int MaxRationaleLength = 1000;
    public const int MaxReviewerLength = 100;

    public Id<Repository> RepositoryId { get; private set; }

    public SignalSourceType SourceType { get; private set; }

    public int SourceNumber { get; private set; }

    public string SourceUrl { get; private set; } = "";

    public DateTimeOffset SourceUpdatedAtUtc { get; private set; }

    public string ContentHash { get; private set; } = "";

    /// <summary>Redacted, truncated title/body projection. Never holds emails, tokens or full threads.</summary>
    public string Excerpt { get; private set; } = "";

    /// <summary>False when the upstream issue/PR was deleted after the suggestion was stored.</summary>
    public bool SourceAvailable { get; private set; }

    /// <summary>Lineage counter per (repository, source type, number). Starts at 1.</summary>
    public int SuggestionVersion { get; private set; }

    public SignalCategory Category { get; private set; }

    public double Confidence { get; private set; }

    public string ClassifierVersion { get; private set; } = "";

    public string Rationale { get; private set; } = "";

    public SignalStatus Status { get; private set; }

    public SignalCategory? CorrectedCategory { get; private set; }

    public string? Reviewer { get; private set; }

    public DateTimeOffset? ReviewedAtUtc { get; private set; }

    /// <summary>Optimistic-concurrency token. Starts at 1, increments on every review.</summary>
    public int Version { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private CommercialSignal() : base() { }

    public CommercialSignal(
        Id<Repository> repositoryId,
        SignalSourceType sourceType,
        int sourceNumber,
        string sourceUrl,
        DateTimeOffset sourceUpdatedAtUtc,
        string contentHash,
        string excerpt,
        int suggestionVersion,
        SignalCategory category,
        double confidence,
        string classifierVersion,
        string rationale,
        SignalStatus status,
        DateTimeOffset now)
        : base(Id<CommercialSignal>.New())
    {
        if (sourceNumber <= 0) throw new DomainException("validation", "Source number must be positive.");
        if (string.IsNullOrWhiteSpace(sourceUrl)) throw new DomainException("validation", "Source URL is required.");
        if (string.IsNullOrWhiteSpace(contentHash)) throw new DomainException("validation", "Content hash is required.");
        if (excerpt.Length > MaxExcerptLength) throw new DomainException("validation", $"Excerpt must be {MaxExcerptLength} characters or fewer.");
        if (rationale.Length > MaxRationaleLength) throw new DomainException("validation", $"Rationale must be {MaxRationaleLength} characters or fewer.");
        if (confidence is < 0 or > 1) throw new DomainException("validation", "Confidence must be in [0,1].");
        if (suggestionVersion < 1) throw new DomainException("validation", "Suggestion version must start at 1.");

        RepositoryId = repositoryId;
        SourceType = sourceType;
        SourceNumber = sourceNumber;
        SourceUrl = sourceUrl;
        SourceUpdatedAtUtc = sourceUpdatedAtUtc;
        ContentHash = contentHash;
        Excerpt = excerpt;
        SourceAvailable = true;
        SuggestionVersion = suggestionVersion;
        Category = category;
        Confidence = confidence;
        ClassifierVersion = classifierVersion;
        Rationale = rationale;
        Status = status;
        Version = 1;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
    }

    /// <summary>Apply a human review. Only Pending (or NeedsRetry via re-review) transitions are allowed.</summary>
    public void ApplyReview(ReviewDecision decision, SignalCategory? correctedCategory, string reviewer, DateTimeOffset now)
    {
        if (Status is not (SignalStatus.Pending or SignalStatus.NeedsRetry))
        {
            throw new DomainException("conflict", $"Signal is already {Status}; reclassification creates a new version instead.");
        }
        if (string.IsNullOrWhiteSpace(reviewer) || reviewer.Length > MaxReviewerLength)
        {
            throw new DomainException("validation", "Reviewer is required (max 100 characters).");
        }
        if (decision == ReviewDecision.Correct)
        {
            if (correctedCategory is null)
            {
                throw new DomainException("validation", "A corrected category is required when correcting.");
            }
            if (correctedCategory == SignalCategory.Unclassified)
            {
                throw new DomainException("validation", "Unclassified is retryable and cannot be a corrected category.");
            }
            CorrectedCategory = correctedCategory;
            Status = SignalStatus.Corrected;
        }
        else
        {
            if (correctedCategory is not null)
            {
                throw new DomainException("validation", "A corrected category is only allowed when correcting.");
            }
            Status = decision == ReviewDecision.Accept ? SignalStatus.Accepted : SignalStatus.Rejected;
        }
        Reviewer = reviewer;
        ReviewedAtUtc = now;
        Version += 1;
        UpdatedAtUtc = now;
    }

    public void MarkSourceUnavailable(DateTimeOffset now)
    {
        SourceAvailable = false;
        UpdatedAtUtc = now;
    }
}
