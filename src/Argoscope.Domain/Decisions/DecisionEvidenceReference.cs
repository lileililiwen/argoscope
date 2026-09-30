using Argoscope.Domain.Common;

namespace Argoscope.Domain.Decisions;

/// <summary>
/// Typed evidence reference attached to a <see cref="DecisionRevision"/>.
/// The reference is resolved at read time by joining to the appropriate
/// store; when the target is missing or cross-portfolio, the API
/// surfaces it with <see cref="DecisionEvidenceResolution.Unresolved"/>
/// rather than silently dropping the row.
/// </summary>
public sealed class DecisionEvidenceReference : Entity<Id<DecisionEvidenceReference>>
{
    public const int MaxLabelLength = 200;

    public Id<DecisionRevision> DecisionRevisionId { get; private set; }

    public DecisionEvidenceKind Kind { get; private set; }

    /// <summary>Stable id of the referenced target (snapshot, observation, etc.).</summary>
    public Guid ReferenceId { get; private set; }

    public string? Label { get; private set; }

    private DecisionEvidenceReference() : base() { }

    public DecisionEvidenceReference(
        Id<DecisionRevision> decisionRevisionId,
        DecisionEvidenceKind kind,
        Guid referenceId,
        string? label)
        : base(Id<DecisionEvidenceReference>.New())
    {
        if (referenceId == Guid.Empty)
        {
            throw new DomainException("validation", "Evidence reference id is required.");
        }
        if (label is { Length: > MaxLabelLength })
        {
            throw new DomainException(
                "validation",
                $"Evidence label must be {MaxLabelLength} characters or fewer.");
        }

        DecisionRevisionId = decisionRevisionId;
        Kind = kind;
        ReferenceId = referenceId;
        Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
    }
}
