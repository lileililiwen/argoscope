using Argoscope.Domain.Common;

namespace Argoscope.Domain.Decisions;

/// <summary>
/// Append-only audit row for a <see cref="DecisionEntry"/>. The history
/// is preserved even when the entry is soft-deleted or restored; the
/// latest revision's after-image is the entry's current state.
/// </summary>
public sealed class DecisionRevision : Entity<Id<DecisionRevision>>
{
    public Id<DecisionEntry> DecisionEntryId { get; private set; }

    public int RevisionNumber { get; private set; }

    public DecisionRevisionAction Action { get; private set; }

    /// <summary>Identifier of the actor that produced this revision.</summary>
    public string ActorId { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    /// <summary>JSON snapshot of the entry before the action (null for Create).</summary>
    public string? BeforeJson { get; private set; }

    /// <summary>JSON snapshot of the entry after the action.</summary>
    public string AfterJson { get; private set; }

    /// <summary>Free-form reason for the change (defaults to the rationale on Create).</summary>
    public string? Note { get; private set; }

    private DecisionRevision() : base() { }

    public DecisionRevision(
        Id<DecisionEntry> decisionEntryId,
        int revisionNumber,
        DecisionRevisionAction action,
        string actorId,
        DateTimeOffset occurredAtUtc,
        string? beforeJson,
        string afterJson,
        string? note)
        : base(Id<DecisionRevision>.New())
    {
        if (revisionNumber <= 0)
        {
            throw new DomainException("validation", "Revision number must be positive.");
        }
        if (string.IsNullOrWhiteSpace(actorId))
        {
            throw new DomainException("validation", "Actor id is required.");
        }
        if (string.IsNullOrWhiteSpace(afterJson))
        {
            throw new DomainException("validation", "Revision after-image is required.");
        }

        DecisionEntryId = decisionEntryId;
        RevisionNumber = revisionNumber;
        Action = action;
        ActorId = actorId.Trim();
        OccurredAtUtc = occurredAtUtc;
        BeforeJson = string.IsNullOrWhiteSpace(beforeJson) ? null : beforeJson;
        AfterJson = afterJson;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }
}
