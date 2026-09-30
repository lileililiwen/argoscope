namespace Argoscope.Domain.Signals;

/// <summary>Advisory commercial-interest categories. Unclassified is retryable; never confirmed.</summary>
public enum SignalCategory
{
    HostedRequest = 0,
    PaidSupport = 1,
    EnterpriseCapability = 2,
    ProcurementQuestion = 3,
    NotCommercial = 4,
    Unclear = 5,
    Unclassified = 6,
}

/// <summary>Eligible source kinds in v1. Comments are excluded.</summary>
public enum SignalSourceType
{
    Issue = 0,
    PullRequest = 1,
}

/// <summary>
/// Lifecycle of one suggestion version. NeedsRetry marks a retryable
/// classifier/provider failure; human review moves Pending to a terminal state.
/// </summary>
public enum SignalStatus
{
    Pending = 0,
    Accepted = 1,
    Rejected = 2,
    Corrected = 3,
    NeedsRetry = 4,
}

/// <summary>Owner review transitions out of Pending.</summary>
public enum ReviewDecision
{
    Accept = 0,
    Reject = 1,
    Correct = 2,
}
