using Argoscope.Domain.Common;
using Argoscope.Domain.Decisions;
using Argoscope.Domain.Portfolios;
using Argoscope.Domain.Repositories;

namespace Argoscope.Application.Decisions;

/// <summary>
/// Inputs for creating a new owner-authored decision. <see cref="IdempotencyKey"/>
/// is optional but recommended for clients that may retry the same POST.
/// </summary>
public sealed record CreateDecisionCommand(
    Id<Portfolio> PortfolioId,
    Id<Repository>? RepositoryId,
    string DecisionType,
    DateOnly DecisionDate,
    string Rationale,
    DateOnly? ReviewDate,
    string? IdempotencyKey,
    string? Note,
    IReadOnlyList<CreateDecisionEvidenceCommand> Evidence,
    string ActorId,
    DateTimeOffset Now);

public sealed record CreateDecisionEvidenceCommand(
    string Kind,
    Guid ReferenceId,
    string? Label);

public sealed record UpdateDecisionCommand(
    Id<Portfolio> PortfolioId,
    Id<DecisionEntry> DecisionEntryId,
    int ExpectedRevision,
    string DecisionType,
    DateOnly DecisionDate,
    string Rationale,
    DateOnly? ReviewDate,
    string? Note,
    IReadOnlyList<CreateDecisionEvidenceCommand> Evidence,
    string ActorId,
    DateTimeOffset Now);

public sealed record DeleteDecisionCommand(
    Id<Portfolio> PortfolioId,
    Id<DecisionEntry> DecisionEntryId,
    int ExpectedRevision,
    string? Note,
    string ActorId,
    DateTimeOffset Now);

public sealed record RestoreDecisionCommand(
    Id<Portfolio> PortfolioId,
    Id<DecisionEntry> DecisionEntryId,
    int ExpectedRevision,
    string? Note,
    string ActorId,
    DateTimeOffset Now);

/// <summary>Read-side DTO for a decision entry, including the latest revision's evidence.</summary>
public sealed record DecisionEntryDto(
    Guid DecisionEntryId,
    Guid PortfolioId,
    Guid? RepositoryId,
    string DecisionType,
    DateOnly DecisionDate,
    string Rationale,
    DateOnly? ReviewDate,
    int RevisionNumber,
    string? IdempotencyKey,
    DateTimeOffset? DeletedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<DecisionEvidenceDto> Evidence,
    DecisionRevisionSummaryDto LatestRevision);

public sealed record DecisionRevisionSummaryDto(
    int RevisionNumber,
    string Action,
    string ActorId,
    DateTimeOffset OccurredAtUtc,
    string? Note);

/// <summary>
/// Read-side DTO for a single evidence reference, including the
/// resolution status computed at read time. The <see cref="SourceDestination"/>
/// describes what the reference points at so the UI can render a label
/// even when the target row is gone.
/// </summary>
public sealed record DecisionEvidenceDto(
    Guid EvidenceId,
    string Kind,
    Guid ReferenceId,
    string Resolution,
    string SourceDestination,
    string? Label,
    DateOnly? EvidenceDate);

/// <summary>One row in the chronological revision history.</summary>
public sealed record DecisionRevisionDto(
    int RevisionNumber,
    string Action,
    string ActorId,
    DateTimeOffset OccurredAtUtc,
    string? Note,
    IReadOnlyList<DecisionEvidenceDto> Evidence);
