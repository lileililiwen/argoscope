using Argoscope.Domain.Common;

namespace Argoscope.Domain.Alerts;

/// <summary>
/// One logical alert evaluation for (rule, metric window end).
/// The unique key prevents duplicate logical alerts when the
/// evaluator job is retried.
/// </summary>
public sealed class AlertEvaluation : Entity<Id<AlertEvaluation>>
{
    public Id<AlertRule> RuleId { get; private set; }

    public Id<Portfolios.Portfolio> PortfolioId { get; private set; }

    public Id<Repositories.Repository>? RepositoryId { get; private set; }

    public string MetricKey { get; private set; }

    public DateTimeOffset MetricWindowEndUtc { get; private set; }

    public double? MetricValue { get; private set; }

    public double Coverage { get; private set; }

    public AlertEvaluationStatus Status { get; private set; }

    public string? Reason { get; private set; }

    public DateTimeOffset EvaluatedAtUtc { get; private set; }

    private AlertEvaluation() : base() { }

    public AlertEvaluation(
        Id<AlertRule> ruleId,
        Id<Portfolios.Portfolio> portfolioId,
        Id<Repositories.Repository>? repositoryId,
        string metricKey,
        DateTimeOffset metricWindowEndUtc,
        double? metricValue,
        double coverage,
        AlertEvaluationStatus status,
        string? reason,
        DateTimeOffset evaluatedAtUtc)
        : base(Id<AlertEvaluation>.New())
    {
        RuleId = ruleId;
        PortfolioId = portfolioId;
        RepositoryId = repositoryId;
        MetricKey = metricKey;
        MetricWindowEndUtc = metricWindowEndUtc;
        MetricValue = metricValue;
        Coverage = coverage;
        Status = status;
        Reason = reason;
        EvaluatedAtUtc = evaluatedAtUtc;
    }

    public static string EvaluationKey(Id<AlertRule> ruleId, DateTimeOffset metricWindowEndUtc) =>
        $"{ruleId.Value:D}:{metricWindowEndUtc.UtcTicks}";
}

/// <summary>
/// One delivery attempt for an evaluation. Retries are bounded;
/// every attempt is recorded even when the logical alert is unique.
/// </summary>
public sealed class DeliveryAttempt : Entity<Id<DeliveryAttempt>>
{
    public const int MaxAttempts = 5;

    public Id<AlertEvaluation> EvaluationId { get; private set; }

    public int AttemptNumber { get; private set; }

    public DeliveryState State { get; private set; }

    public int? ResponseCode { get; private set; }

    public string? Error { get; private set; }

    public DateTimeOffset? NextRetryAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private DeliveryAttempt() : base() { }

    public DeliveryAttempt(
        Id<AlertEvaluation> evaluationId,
        int attemptNumber,
        DeliveryState state,
        int? responseCode,
        string? error,
        DateTimeOffset? nextRetryAtUtc,
        DateTimeOffset now)
        : base(Id<DeliveryAttempt>.New())
    {
        EvaluationId = evaluationId;
        AttemptNumber = attemptNumber;
        State = state;
        ResponseCode = responseCode;
        Error = error;
        NextRetryAtUtc = nextRetryAtUtc;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
    }

    public void Mark(DeliveryState state, int? responseCode, string? error, DateTimeOffset? nextRetryAtUtc, DateTimeOffset now)
    {
        State = state;
        ResponseCode = responseCode;
        Error = error;
        NextRetryAtUtc = nextRetryAtUtc;
        UpdatedAtUtc = now;
    }

    /// <summary>Bounded exponential backoff: 1m, 4m, 9m, 16m.</summary>
    public static DateTimeOffset? ComputeNextRetry(int attemptNumber, DateTimeOffset now) =>
        attemptNumber >= MaxAttempts
            ? null
            : now.AddMinutes(attemptNumber * attemptNumber);
}
