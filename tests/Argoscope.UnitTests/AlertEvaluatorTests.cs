using Argoscope.Application.Alerts;
using Argoscope.Domain.Alerts;
using Xunit;

namespace Argoscope.UnitTests;

public sealed class AlertEvaluatorTests
{
    [Fact]
    public void ThresholdEquality_FiresOnlyForInclusiveOperators()
    {
        var atThreshold = 10d;
        Assert.True(AlertEvaluator.Evaluate(new AlertEvaluator.GateInput(
            atThreshold, 1d, 0.5d, AlertOperator.GreaterThanOrEqual, 10d)).ShouldFire);
        Assert.True(AlertEvaluator.Evaluate(new AlertEvaluator.GateInput(
            atThreshold, 1d, 0.5d, AlertOperator.LessThanOrEqual, 10d)).ShouldFire);
        Assert.False(AlertEvaluator.Evaluate(new AlertEvaluator.GateInput(
            atThreshold, 1d, 0.5d, AlertOperator.GreaterThan, 10d)).ShouldFire);
        Assert.False(AlertEvaluator.Evaluate(new AlertEvaluator.GateInput(
            atThreshold, 1d, 0.5d, AlertOperator.LessThan, 10d)).ShouldFire);
    }

    [Fact]
    public void MissingMetric_NeverFires()
    {
        var outcome = AlertEvaluator.Evaluate(new AlertEvaluator.GateInput(
            null, 0d, 0.5d, AlertOperator.GreaterThanOrEqual, 1d));
        Assert.False(outcome.ShouldFire);
        Assert.Equal("metric-unavailable", outcome.SkipReason);
    }

    [Fact]
    public void LowCoverage_NeverFires()
    {
        var outcome = AlertEvaluator.Evaluate(new AlertEvaluator.GateInput(
            100d, 0.2d, 0.5d, AlertOperator.GreaterThanOrEqual, 1d));
        Assert.False(outcome.ShouldFire);
        Assert.Equal("low-coverage", outcome.SkipReason);
    }

    [Fact]
    public void CoverageBoundary_IsInclusive()
    {
        var outcome = AlertEvaluator.Evaluate(new AlertEvaluator.GateInput(
            100d, 0.5d, 0.5d, AlertOperator.GreaterThanOrEqual, 1d));
        Assert.True(outcome.ShouldFire);
    }

    [Fact]
    public void Cooldown_BlocksRepeatFire()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.True(AlertEvaluator.IsCooldownActive(now.AddHours(-1), 24, now));
        Assert.False(AlertEvaluator.IsCooldownActive(now.AddHours(-25), 24, now));
        Assert.False(AlertEvaluator.IsCooldownActive(null, 24, now));
        Assert.False(AlertEvaluator.IsCooldownActive(now.AddHours(-1), 0, now));
    }
}

public sealed class WebhookSafetyTests
{
    [Theory]
    [InlineData("http://example.com/hook")]
    [InlineData("https://localhost/hook")]
    [InlineData("https://127.0.0.1/hook")]
    [InlineData("https://10.0.0.5/hook")]
    [InlineData("https://192.168.1.10/hook")]
    [InlineData("https://172.20.4.1/hook")]
    [InlineData("https://169.254.169.254/hook")]
    [InlineData("https://[::1]/hook")]
    [InlineData("https://[fc00::1]/hook")]
    [InlineData("not-a-url")]
    public void UnsafeDestinations_AreRejected(string destination)
    {
        Assert.False(WebhookSafety.CheckStatic(destination).IsAllowed);
    }

    [Theory]
    [InlineData("https://example.com/hook")]
    [InlineData("https://hooks.example.org:443/alerts")]
    public void PublicHttpsDestinations_AreAllowed(string destination)
    {
        Assert.True(WebhookSafety.CheckStatic(destination).IsAllowed);
    }
}

public sealed class DeliveryPayloadSignerTests
{
    [Fact]
    public void Sign_IsDeterministicAndPrefixed()
    {
        var first = DeliveryPayloadSigner.SignHmacSha256("{\"a\":1}", "secret");
        var second = DeliveryPayloadSigner.SignHmacSha256("{\"a\":1}", "secret");
        Assert.Equal(first, second);
        Assert.StartsWith("sha256=", first);
        Assert.NotEqual(first, DeliveryPayloadSigner.SignHmacSha256("{\"a\":2}", "secret"));
    }
}
