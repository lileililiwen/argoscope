using System.Text.Json.Serialization;
using Argoscope.Domain.Common;

namespace Argoscope.Domain.Decisions;

/// <summary>
/// Owner-authored classification of a portfolio or repository decision. The
/// list is the single source of truth for the decision_type column; adding
/// a new value requires a schema change in the migrations because the
/// database stores it as an integer.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DecisionType
{
    /// <summary>Owner chose to keep investing at the current level.</summary>
    Continue = 0,
    /// <summary>Owner chose to increase investment (paid support, hosted, etc.).</summary>
    Invest = 1,
    /// <summary>Owner chose to pause active work without archiving.</summary>
    Pause = 2,
    /// <summary>Owner chose to archive the project.</summary>
    Archive = 3,
    /// <summary>Owner flagged the project for a future revisit (date in the future).</summary>
    Revisit = 4,
}

/// <summary>
/// Action recorded on each <see cref="DecisionRevision"/>. The
/// revision number is monotonic per entry; the action tells readers
/// whether the snapshot reflects the initial create, a content edit, a
/// soft-delete, or a restore.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DecisionRevisionAction
{
    Create = 0,
    Update = 1,
    Delete = 2,
    Restore = 3,
}

/// <summary>
/// Kinds of typed evidence reference that an owner can attach to a
/// decision. The argoscope codebase only resolves
/// <see cref="Snapshot"/> and <see cref="PackageObservation"/> today;
/// <see cref="CommercialSignal"/> is accepted as a kind and surfaced as
/// <c>Unavailable</c> until the commercial-signal-review change lands.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DecisionEvidenceKind
{
    /// <summary>Reference is a <c>MetricSnapshot</c> id.</summary>
    Snapshot = 0,
    /// <summary>Reference is a <c>PackageObservation</c> id.</summary>
    PackageObservation = 1,
    /// <summary>Reference is a future <c>CommercialSignal</c> id.</summary>
    CommercialSignal = 2,
}

/// <summary>
/// State the API returns for a single evidence reference. <see cref="Resolved"/>
/// means the target exists in the same portfolio. <see cref="Unresolved"/>
/// means the id is structurally valid but the target row cannot be located
/// (deleted, never collected, or wrong portfolio). The frontend renders
/// <c>Unresolved</c> as a broken-link marker without dropping the reference.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DecisionEvidenceResolution
{
    Resolved = 0,
    Unresolved = 1,
}
