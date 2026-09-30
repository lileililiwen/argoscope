namespace Argoscope.Domain.Alerts;

/// <summary>Comparison operator for an alert rule threshold.</summary>
public enum AlertOperator
{
    Unknown = 0,
    GreaterThan = 1,
    GreaterThanOrEqual = 2,
    LessThan = 3,
    LessThanOrEqual = 4,
}

/// <summary>Delivery channel for an alert rule.</summary>
public enum AlertChannel
{
    Unknown = 0,
    Email = 1,
    Webhook = 2,
}

/// <summary>Outcome of a single rule evaluation.</summary>
public enum AlertEvaluationStatus
{
    Unknown = 0,
    Fired = 1,
    SkippedInsufficientData = 2,
    SkippedCooldown = 3,
    SkippedDisabled = 4,
}

/// <summary>Lifecycle state of one delivery attempt.</summary>
public enum DeliveryState
{
    Unknown = 0,
    Queued = 1,
    Sending = 2,
    Delivered = 3,
    RetryableFailure = 4,
    PermanentFailure = 5,
    Canceled = 6,
}
