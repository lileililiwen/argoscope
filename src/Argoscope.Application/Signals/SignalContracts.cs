using Argoscope.Domain.Common;
using Argoscope.Domain.Repositories;
using Argoscope.Domain.Signals;

namespace Argoscope.Application.Signals;

public sealed record CollectSignalsCommand(
    Id<Repository> RepositoryId,
    DateTimeOffset Now);

public sealed record ReviewSignalCommand(
    Id<Repository> RepositoryId,
    Id<CommercialSignal> SignalId,
    int ExpectedVersion,
    string Decision,
    string? CorrectedCategory,
    string? Note,
    string Reviewer,
    DateTimeOffset Now);

public sealed record CommercialSignalDto(
    Guid SignalId,
    Guid RepositoryId,
    string SourceType,
    int SourceNumber,
    string SourceUrl,
    DateTimeOffset SourceUpdatedAtUtc,
    string ContentHash,
    string Excerpt,
    bool SourceAvailable,
    int SuggestionVersion,
    string Category,
    double Confidence,
    string ClassifierVersion,
    string Rationale,
    string Status,
    string? CorrectedCategory,
    string? Reviewer,
    DateTimeOffset? ReviewedAtUtc,
    int Version,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record SignalReviewDto(
    int RevisionNumber,
    string Decision,
    string PriorCategory,
    string PriorStatus,
    string? CorrectedCategory,
    string Reviewer,
    DateTimeOffset OccurredAtUtc,
    string? Note);

public sealed record CollectSignalsResult(
    int Created,
    int Duplicates,
    int Retried,
    int MarkedUnavailable);
