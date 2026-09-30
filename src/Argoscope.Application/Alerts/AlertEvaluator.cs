using Argoscope.Domain.Alerts;

namespace Argoscope.Application.Alerts;

/// <summary>
/// Pure threshold/coverage/cooldown gate. Missing or under-covered
/// metrics never fire; threshold equality fires only for the
/// inclusive operators.
/// </summary>
public static class AlertEvaluator
{
    public sealed record GateInput(
        double? MetricValue,
        double Coverage,
        double MinimumCoverage,
        AlertOperator Operator,
        double Threshold);

    public sealed record GateOutcome(bool ShouldFire, string? SkipReason);

    public static GateOutcome Evaluate(GateInput input)
    {
        if (input.MetricValue is null || double.IsNaN(input.MetricValue.Value) || double.IsInfinity(input.MetricValue.Value))
        {
            return new GateOutcome(false, "metric-unavailable");
        }
        if (input.Coverage < input.MinimumCoverage)
        {
            return new GateOutcome(false, "low-coverage");
        }
        var fires = input.Operator switch
        {
            AlertOperator.GreaterThan => input.MetricValue.Value > input.Threshold,
            AlertOperator.GreaterThanOrEqual => input.MetricValue.Value >= input.Threshold,
            AlertOperator.LessThan => input.MetricValue.Value < input.Threshold,
            AlertOperator.LessThanOrEqual => input.MetricValue.Value <= input.Threshold,
            _ => false,
        };
        return fires
            ? new GateOutcome(true, null)
            : new GateOutcome(false, "threshold-not-met");
    }

    public static bool IsCooldownActive(DateTimeOffset? lastFiredAtUtc, int cooldownHours, DateTimeOffset now)
    {
        if (lastFiredAtUtc is null || cooldownHours <= 0)
        {
            return false;
        }
        return now < lastFiredAtUtc.Value.AddHours(cooldownHours);
    }
}
