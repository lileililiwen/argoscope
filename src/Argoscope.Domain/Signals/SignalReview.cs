using Argoscope.Domain.Common;

namespace Argoscope.Domain.Signals;

/// <summary>
/// Immutable audit event for one review transition. Rows are append-only;
/// the service is the only writer.
/// </summary>
public sealed class SignalReview : Entity<Id<SignalReview>>
{
    public const int MaxNoteLength = 2000;

    public Id<CommercialSignal> SignalId { get; private set; }

    /// <summary>The signal <see cref="CommercialSignal.Version"/> after the transition.</summary>
    public int RevisionNumber { get; private set; }

    public ReviewDecision Decision { get; private set; }

    public SignalCategory PriorCategory { get; private set; }

    public SignalStatus PriorStatus { get; private set; }

    public SignalCategory? CorrectedCategory { get; private set; }

    public string Reviewer { get; private set; } = "";

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public string? Note { get; private set; }

    private SignalReview() : base() { }

    public SignalReview(
        Id<CommercialSignal> signalId,
        int revisionNumber,
        ReviewDecision decision,
        SignalCategory priorCategory,
        SignalStatus priorStatus,
        SignalCategory? correctedCategory,
        string reviewer,
        DateTimeOffset occurredAtUtc,
        string? note)
        : base(Id<SignalReview>.New())
    {
        if (note is not null && note.Length > MaxNoteLength)
        {
            throw new DomainException("validation", $"Note must be {MaxNoteLength} characters or fewer.");
        }
        SignalId = signalId;
        RevisionNumber = revisionNumber;
        Decision = decision;
        PriorCategory = priorCategory;
        PriorStatus = priorStatus;
        CorrectedCategory = correctedCategory;
        Reviewer = reviewer;
        OccurredAtUtc = occurredAtUtc;
        Note = note;
    }
}
