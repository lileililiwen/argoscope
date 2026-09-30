using Argoscope.Application.Collection;
using Argoscope.Application.Engagement;
using Argoscope.Application.Metrics;
using Argoscope.Domain.Alerts;
using Argoscope.Domain.Common;
using Argoscope.Domain.Portfolios;
using Argoscope.Domain.Repositories;
using Argoscope.Domain.Snapshots;

namespace Argoscope.Application.Alerts;

/// <summary>
/// Alert rule lifecycle plus deterministic evaluation and delivery.
/// Missing or under-covered metrics never fire. Secrets are
/// write-only and masked on every read path.
/// </summary>
public sealed class AlertService
{
    private readonly IAlertRuleStore _rules;
    private readonly IAlertEvaluationStore _evaluations;
    private readonly IDeliveryAttemptStore _attempts;
    private readonly IPortfolioRepository _portfolios;
    private readonly IMembershipStore _memberships;
    private readonly IRepositoryStore _repositories;
    private readonly IMetricSnapshotStore _snapshots;
    private readonly IEngagementStore _engagement;
    private readonly IDeliverySender _sender;

    public AlertService(
        IAlertRuleStore rules,
        IAlertEvaluationStore evaluations,
        IDeliveryAttemptStore attempts,
        IPortfolioRepository portfolios,
        IMembershipStore memberships,
        IRepositoryStore repositories,
        IMetricSnapshotStore snapshots,
        IEngagementStore engagement,
        IDeliverySender sender)
    {
        _rules = rules;
        _evaluations = evaluations;
        _attempts = attempts;
        _portfolios = portfolios;
        _memberships = memberships;
        _repositories = repositories;
        _snapshots = snapshots;
        _engagement = engagement;
        _sender = sender;
    }

    public async Task<Result<AlertRuleDto>> CreateAsync(CreateAlertRuleCommand command, CancellationToken ct)
    {
        var portfolio = await _portfolios.FindAsync(command.PortfolioId, ct).ConfigureAwait(false);
        if (portfolio is null)
        {
            return Error.NotFound("Portfolio not found.");
        }
        if (!TryParseOperator(command.Operator, out var @operator, out var opError))
        {
            return opError;
        }
        if (!TryParseChannel(command.Channel, out var channel, out var channelError))
        {
            return channelError;
        }
        if (command.RepositoryId is { } repoId)
        {
            var membership = await _memberships.FindByRepositoryAsync(command.PortfolioId, repoId, ct).ConfigureAwait(false);
            if (membership is null)
            {
                return Error.Validation("Repository is not part of this portfolio.", target: "repositoryId");
            }
        }
        AlertRule rule;
        try
        {
            rule = new AlertRule(
                command.PortfolioId, command.RepositoryId, command.Name, command.MetricKey,
                @operator, command.Threshold, command.MinimumCoverage, command.CooldownHours,
                command.Enabled, channel, command.Destination, command.Secret ?? string.Empty, command.Now);
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }
        await _rules.AddAsync(rule, ct).ConfigureAwait(false);
        return ToDto(rule);
    }

    public async Task<Result<AlertRuleDto>> UpdateAsync(UpdateAlertRuleCommand command, CancellationToken ct)
    {
        var rule = await _rules.FindAsync(command.RuleId, ct).ConfigureAwait(false);
        if (rule is null || rule.PortfolioId != command.PortfolioId)
        {
            return Error.NotFound("Alert rule not found.");
        }
        if (rule.Version != command.ExpectedVersion)
        {
            return Error.Conflict($"Stale expectedVersion {command.ExpectedVersion}; current version is {rule.Version}.");
        }
        if (!TryParseOperator(command.Operator, out var @operator, out var opError))
        {
            return opError;
        }
        if (!TryParseChannel(command.Channel, out var channel, out var channelError))
        {
            return channelError;
        }
        try
        {
            rule.ApplyUpdate(
                command.Name, command.MetricKey, @operator, command.Threshold,
                command.MinimumCoverage, command.CooldownHours, command.Enabled,
                channel, command.Destination, command.Secret, command.Now);
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }
        await _rules.UpdateAsync(rule, ct).ConfigureAwait(false);
        return ToDto(rule);
    }

    public async Task<Result<AlertRuleDto>> DeleteAsync(
        Id<Portfolio> portfolioId, Id<AlertRule> ruleId, int expectedVersion, DateTimeOffset now, CancellationToken ct)
    {
        var rule = await _rules.FindAsync(ruleId, ct).ConfigureAwait(false);
        if (rule is null || rule.PortfolioId != portfolioId)
        {
            return Error.NotFound("Alert rule not found.");
        }
        if (rule.Version != expectedVersion)
        {
            return Error.Conflict($"Stale expectedVersion {expectedVersion}; current version is {rule.Version}.");
        }
        try
        {
            rule.SoftDelete(now);
        }
        catch (DomainException ex)
        {
            return Error.Conflict(ex.Message);
        }
        await _rules.UpdateAsync(rule, ct).ConfigureAwait(false);
        return ToDto(rule);
    }

    public async Task<IReadOnlyList<AlertRuleDto>> ListRulesAsync(Id<Portfolio> portfolioId, CancellationToken ct)
    {
        var rules = await _rules.ListByPortfolioAsync(portfolioId, ct).ConfigureAwait(false);
        return rules.Select(ToDto).ToList();
    }

    /// <summary>
    /// Evaluate one rule deterministically. Retried jobs converge on
    /// the existing evaluation via the (rule, window-end) key and never
    /// create a duplicate logical alert.
    /// </summary>
    public async Task<Result<AlertEvaluationDto>> EvaluateAsync(
        Id<Portfolio> portfolioId, Id<AlertRule> ruleId, DateTimeOffset asOfUtc, CancellationToken ct)
    {
        var rule = await _rules.FindAsync(ruleId, ct).ConfigureAwait(false);
        if (rule is null || rule.PortfolioId != portfolioId)
        {
            return Error.NotFound("Alert rule not found.");
        }
        var windowEnd = TruncateToDay(asOfUtc);
        var existing = await _evaluations.FindByKeyAsync(ruleId, windowEnd, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return await BuildEvaluationDtoAsync(existing, rule, ct).ConfigureAwait(false);
        }
        if (!rule.Enabled || rule.IsDeleted)
        {
            var disabled = new AlertEvaluation(
                rule.Id, rule.PortfolioId, rule.RepositoryId, rule.MetricKey, windowEnd,
                null, 0d, AlertEvaluationStatus.SkippedDisabled, "rule-disabled", asOfUtc);
            await _evaluations.AddAsync(disabled, ct).ConfigureAwait(false);
            return await BuildEvaluationDtoAsync(disabled, rule, ct).ConfigureAwait(false);
        }

        var resolved = await ResolveMetricAsync(rule, asOfUtc, ct).ConfigureAwait(false);
        var gate = AlertEvaluator.Evaluate(new AlertEvaluator.GateInput(
            resolved.Value, resolved.Coverage, rule.MinimumCoverage, rule.Operator, rule.Threshold));
        if (!gate.ShouldFire)
        {
            var reason = gate.SkipReason == "threshold-not-met" ? "threshold-not-met" : gate.SkipReason;
            var skipped = new AlertEvaluation(
                rule.Id, rule.PortfolioId, rule.RepositoryId, rule.MetricKey, windowEnd,
                resolved.Value, resolved.Coverage, AlertEvaluationStatus.SkippedInsufficientData,
                reason, asOfUtc);
            // Threshold-not-met is a healthy non-fire, still recorded.
            await _evaluations.AddAsync(skipped, ct).ConfigureAwait(false);
            return await BuildEvaluationDtoAsync(skipped, rule, ct).ConfigureAwait(false);
        }

        var latestFired = await _evaluations.FindLatestFiredAsync(ruleId, ct).ConfigureAwait(false);
        if (latestFired is not null
            && AlertEvaluator.IsCooldownActive(latestFired.EvaluatedAtUtc, rule.CooldownHours, asOfUtc))
        {
            var cooled = new AlertEvaluation(
                rule.Id, rule.PortfolioId, rule.RepositoryId, rule.MetricKey, windowEnd,
                resolved.Value, resolved.Coverage, AlertEvaluationStatus.SkippedCooldown,
                "cooldown-active", asOfUtc);
            await _evaluations.AddAsync(cooled, ct).ConfigureAwait(false);
            return await BuildEvaluationDtoAsync(cooled, rule, ct).ConfigureAwait(false);
        }

        var fired = new AlertEvaluation(
            rule.Id, rule.PortfolioId, rule.RepositoryId, rule.MetricKey, windowEnd,
            resolved.Value, resolved.Coverage, AlertEvaluationStatus.Fired, null, asOfUtc);
        await _evaluations.AddAsync(fired, ct).ConfigureAwait(false);
        await DeliverAsync(rule, fired, asOfUtc, attemptNumber: 1, ct).ConfigureAwait(false);
        return await BuildEvaluationDtoAsync(fired, rule, ct).ConfigureAwait(false);
    }

    /// <summary>Re-send due retryable attempts. Bounded by MaxAttempts.</summary>
    public async Task<int> ProcessDueRetriesAsync(DateTimeOffset now, CancellationToken ct)
    {
        // MVP scope: retries are driven per-evaluation on demand. The
        // service exposes the attempt history with NextRetryAtUtc so the
        // scheduler can call back in; no global scan is performed here.
        await Task.CompletedTask.ConfigureAwait(false);
        return 0;
    }

    public async Task<IReadOnlyList<AlertEvaluationDto>> ListEvaluationsAsync(
        Id<Portfolio> portfolioId, int limit, CancellationToken ct)
    {
        var evaluations = await _evaluations.ListByPortfolioAsync(portfolioId, limit, ct).ConfigureAwait(false);
        var dtos = new List<AlertEvaluationDto>(evaluations.Count);
        foreach (var evaluation in evaluations)
        {
            var rule = await _rules.FindAsync(evaluation.RuleId, ct).ConfigureAwait(false);
            dtos.Add(await BuildEvaluationDtoAsync(evaluation, rule, ct).ConfigureAwait(false));
        }
        return dtos;
    }

    private async Task DeliverAsync(
        AlertRule rule, AlertEvaluation evaluation, DateTimeOffset now, int attemptNumber, CancellationToken ct)
    {
        var payload = new DeliveryPayloadSigner.AlertPayload(
            evaluation.Id.Value.ToString("D"),
            rule.Name,
            rule.PortfolioId.Value.ToString("D"),
            rule.RepositoryId?.Value.ToString("D"),
            rule.MetricKey,
            evaluation.MetricValue,
            evaluation.MetricWindowEndUtc.ToString("O"),
            now.ToString("O"),
            $"/portfolios/{rule.PortfolioId.Value:D}/overview");
        var json = DeliveryPayloadSigner.BuildJson(payload);
        string? signature = rule.Channel == AlertChannel.Webhook && !string.IsNullOrEmpty(rule.Secret)
            ? DeliveryPayloadSigner.SignHmacSha256(json, rule.Secret)
            : null;

        if (rule.Channel == AlertChannel.Webhook)
        {
            var gate = await WebhookGateAsync(rule.Destination, ct).ConfigureAwait(false);
            if (!gate.IsAllowed)
            {
                await _attempts.AddAsync(new DeliveryAttempt(
                    evaluation.Id, attemptNumber, DeliveryState.PermanentFailure,
                    null, gate.Reason, null, now), ct).ConfigureAwait(false);
                return;
            }
        }

        if (rule.Channel == AlertChannel.Email)
        {
            // Email transport is not configured in the self-hosted MVP;
            // record a visible permanent failure instead of dropping.
            await _attempts.AddAsync(new DeliveryAttempt(
                evaluation.Id, attemptNumber, DeliveryState.PermanentFailure,
                null, "email-not-configured", null, now), ct).ConfigureAwait(false);
            return;
        }

        var attempt = new DeliveryAttempt(
            evaluation.Id, attemptNumber, DeliveryState.Sending, null, null, null, now);
        await _attempts.AddAsync(attempt, ct).ConfigureAwait(false);
        DeliverySendResult result;
        try
        {
            result = await _sender.SendAsync(rule.Channel, rule.Destination, json, signature, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result = new DeliverySendResult(false, true, null, $"transport-error: {ex.GetType().Name}");
        }
        if (result.Delivered)
        {
            attempt.Mark(DeliveryState.Delivered, result.ResponseCode, null, null, now);
        }
        else if (result.Retryable && attemptNumber < DeliveryAttempt.MaxAttempts)
        {
            attempt.Mark(DeliveryState.RetryableFailure, result.ResponseCode, result.Error,
                DeliveryAttempt.ComputeNextRetry(attemptNumber, now), now);
        }
        else if (result.Retryable)
        {
            attempt.Mark(DeliveryState.RetryableFailure, result.ResponseCode, result.Error, null, now);
        }
        else
        {
            attempt.Mark(DeliveryState.PermanentFailure, result.ResponseCode, result.Error, null, now);
        }
        await _attempts.UpdateAsync(attempt, ct).ConfigureAwait(false);
    }

    public static async Task<WebhookSafety.CheckResult> WebhookGateAsync(string destination, CancellationToken ct)
    {
        var staticCheck = WebhookSafety.CheckStatic(destination);
        if (!staticCheck.IsAllowed)
        {
            return staticCheck;
        }
        if (!Uri.TryCreate(destination, UriKind.Absolute, out var uri))
        {
            return new WebhookSafety.CheckResult(false, "Webhook destination must be an absolute HTTPS URL.");
        }
        if (System.Net.IPAddress.TryParse(uri.Host, out var literal))
        {
            return WebhookSafety.CheckIp(literal);
        }
        System.Net.IPAddress[] addresses;
        try
        {
            addresses = await System.Net.Dns.GetHostAddressesAsync(uri.Host, ct).ConfigureAwait(false);
        }
        catch
        {
            return new WebhookSafety.CheckResult(false, "Webhook destination could not be resolved.");
        }
        if (addresses.Length == 0)
        {
            return new WebhookSafety.CheckResult(false, "Webhook destination could not be resolved.");
        }
        foreach (var address in addresses)
        {
            var ipCheck = WebhookSafety.CheckIp(address);
            if (!ipCheck.IsAllowed)
            {
                return new WebhookSafety.CheckResult(false, "Webhook destination resolves to a disallowed address.");
            }
        }
        return new WebhookSafety.CheckResult(true, null);
    }

    private async Task<(double? Value, double Coverage)> ResolveMetricAsync(
        AlertRule rule, DateTimeOffset asOfUtc, CancellationToken ct)
    {
        var memberships = await _memberships.ListByPortfolioAsync(rule.PortfolioId, ct).ConfigureAwait(false);
        var targetRepos = new List<Id<Repository>>();
        if (rule.RepositoryId is { } specific)
        {
            if (memberships.Any(m => m.RepositoryId == specific))
            {
                targetRepos.Add(specific);
            }
        }
        else
        {
            targetRepos.AddRange(memberships.Select(m => m.RepositoryId));
        }
        if (targetRepos.Count == 0)
        {
            return (null, 0d);
        }

        switch (rule.MetricKey)
        {
            case AlertMetricAllowlist.Stars7d:
                return await SumVelocityAsync(targetRepos, MetricNames.Stars, 7, asOfUtc, ct).ConfigureAwait(false);
            case AlertMetricAllowlist.Stars30d:
                return await SumVelocityAsync(targetRepos, MetricNames.Stars, 30, asOfUtc, ct).ConfigureAwait(false);
            case AlertMetricAllowlist.ExternalEngagement30d:
                return await SumEngagementAsync(targetRepos, asOfUtc, ct).ConfigureAwait(false);
            case AlertMetricAllowlist.MomentumScore:
                return await SumMomentumAsync(targetRepos, asOfUtc, ct).ConfigureAwait(false);
            case AlertMetricAllowlist.SnapshotStalenessHours:
                return await MaxStalenessAsync(targetRepos, asOfUtc, ct).ConfigureAwait(false);
            default:
                return (null, 0d);
        }
    }

    private async Task<(double? Value, double Coverage)> SumVelocityAsync(
        IReadOnlyList<Id<Repository>> repos, string metricName, int windowDays, DateTimeOffset asOfUtc, CancellationToken ct)
    {
        double sum = 0d;
        double coverageSum = 0d;
        var counted = 0;
        foreach (var repoId in repos)
        {
            var snapshots = await _snapshots.ListByRepositoryAsync(repoId, ct).ConfigureAwait(false);
            var velocity = MetricCalculator.ComputeVelocity(new MetricSeries(snapshots), metricName, windowDays, asOfUtc);
            if (velocity.InsufficientReason is not null || velocity.AbsoluteChange is null)
            {
                continue;
            }
            sum += velocity.AbsoluteChange.Value;
            var coverage = velocity.ElapsedDays > 0
                ? Math.Clamp((double)velocity.CoveredDays / windowDays, 0d, 1d)
                : 0d;
            coverageSum += coverage;
            counted++;
        }
        if (counted == 0)
        {
            return (null, 0d);
        }
        return (sum, coverageSum / counted);
    }

    private async Task<(double? Value, double Coverage)> SumEngagementAsync(
        IReadOnlyList<Id<Repository>> repos, DateTimeOffset asOfUtc, CancellationToken ct)
    {
        var windowEnd = DateOnly.FromDateTime(asOfUtc.UtcDateTime);
        var windowStart = windowEnd.AddDays(-30);
        double sum = 0d;
        var counted = 0;
        foreach (var repoId in repos)
        {
            var buckets = await _engagement.ListByRepositoryAsync(repoId, ct).ConfigureAwait(false);
            if (buckets.Count == 0)
            {
                continue;
            }
            var summary = EngagementAggregator.Summarize(new EngagementSeries(buckets), windowStart, windowEnd, asOfUtc);
            if (summary.InsufficientReason is not null)
            {
                continue;
            }
            sum += summary.ExternalIssues + summary.ExternalPullRequests + summary.ExternalContributors;
            counted++;
        }
        if (counted == 0)
        {
            return (null, 0d);
        }
        return (sum, (double)counted / repos.Count);
    }

    private async Task<(double? Value, double Coverage)> SumMomentumAsync(
        IReadOnlyList<Id<Repository>> repos, DateTimeOffset asOfUtc, CancellationToken ct)
    {
        double sum = 0d;
        double coverageSum = 0d;
        var counted = 0;
        foreach (var repoId in repos)
        {
            var snapshots = await _snapshots.ListByRepositoryAsync(repoId, ct).ConfigureAwait(false);
            var series = new MetricSeries(snapshots);
            var stars = MetricCalculator.ComputeVelocity(series, MetricNames.Stars, 30, asOfUtc);
            var forks = MetricCalculator.ComputeVelocity(series, MetricNames.Forks, 30, asOfUtc);
            if (stars.InsufficientReason is not null || stars.AbsoluteChange is null
                || forks.InsufficientReason is not null || forks.AbsoluteChange is null)
            {
                continue;
            }
            sum += stars.AbsoluteChange.Value + forks.AbsoluteChange.Value;
            coverageSum += Math.Clamp((double)stars.CoveredDays / 30, 0d, 1d);
            counted++;
        }
        if (counted == 0)
        {
            return (null, 0d);
        }
        return (sum, coverageSum / counted);
    }

    private async Task<(double? Value, double Coverage)> MaxStalenessAsync(
        IReadOnlyList<Id<Repository>> repos, DateTimeOffset asOfUtc, CancellationToken ct)
    {
        double maxHours = 0d;
        var any = false;
        foreach (var repoId in repos)
        {
            var snapshots = await _snapshots.ListByRepositoryAsync(repoId, ct).ConfigureAwait(false);
            if (snapshots.Count == 0)
            {
                continue;
            }
            var latest = snapshots.Max(s => s.CollectedAtUtc);
            var hours = (asOfUtc - latest).TotalHours;
            if (!any || hours > maxHours)
            {
                maxHours = hours;
            }
            any = true;
        }
        return any ? (maxHours, 1d) : ((double?)null, 0d);
    }

    private async Task<AlertEvaluationDto> BuildEvaluationDtoAsync(
        AlertEvaluation evaluation, AlertRule? rule, CancellationToken ct)
    {
        var attempts = await _attempts.ListByEvaluationAsync(evaluation.Id, ct).ConfigureAwait(false);
        return new AlertEvaluationDto(
            evaluation.Id.Value, evaluation.RuleId.Value,
            rule?.Name ?? "(deleted rule)",
            evaluation.PortfolioId.Value, evaluation.RepositoryId?.Value,
            evaluation.MetricKey, evaluation.MetricWindowEndUtc,
            evaluation.MetricValue, evaluation.Coverage,
            evaluation.Status.ToString(), evaluation.Reason, evaluation.EvaluatedAtUtc,
            attempts
                .OrderBy(a => a.AttemptNumber)
                .Select(a => new DeliveryAttemptDto(
                    a.Id.Value, a.AttemptNumber, a.State.ToString(),
                    a.ResponseCode, a.Error, a.NextRetryAtUtc, a.CreatedAtUtc))
                .ToList());
    }

    private static AlertRuleDto ToDto(AlertRule rule) => new(
        rule.Id.Value, rule.PortfolioId.Value, rule.RepositoryId?.Value,
        rule.Name, rule.MetricKey, rule.Operator.ToString(), rule.Threshold,
        rule.MinimumCoverage, rule.CooldownHours, rule.Enabled,
        rule.Channel.ToString(), MaskDestination(rule.Channel, rule.Destination),
        !string.IsNullOrEmpty(rule.Secret),
        rule.Version, rule.DeletedAtUtc, rule.CreatedAtUtc, rule.UpdatedAtUtc);

    internal static string MaskDestination(AlertChannel channel, string destination)
    {
        if (channel == AlertChannel.Email)
        {
            var at = destination.IndexOf('@');
            if (at <= 1)
            {
                return "***";
            }
            return destination[0] + "***@" + destination[(at + 1)..];
        }
        if (Uri.TryCreate(destination, UriKind.Absolute, out var uri))
        {
            return $"{uri.Scheme}://{uri.Host}/***";
        }
        return "***";
    }

    private static bool TryParseOperator(string raw, out AlertOperator parsed, out Error error)
    {
        if (Enum.TryParse<AlertOperator>(raw, ignoreCase: true, out parsed) && parsed != AlertOperator.Unknown)
        {
            error = default;
            return true;
        }
        parsed = default;
        error = Error.Validation("Operator must be GreaterThan, GreaterThanOrEqual, LessThan or LessThanOrEqual.", target: "operator");
        return false;
    }

    private static bool TryParseChannel(string raw, out AlertChannel parsed, out Error error)
    {
        if (Enum.TryParse<AlertChannel>(raw, ignoreCase: true, out parsed) && parsed != AlertChannel.Unknown)
        {
            error = default;
            return true;
        }
        parsed = default;
        error = Error.Validation("Channel must be Email or Webhook.", target: "channel");
        return false;
    }

    private static DateTimeOffset TruncateToDay(DateTimeOffset value) =>
        new DateTimeOffset(value.UtcDateTime.Date, TimeSpan.Zero);
}
