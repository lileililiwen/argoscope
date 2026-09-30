using Argoscope.Application.Alerts;
using Argoscope.Domain.Alerts;
using Argoscope.Domain.Common;
using Argoscope.Domain.Portfolios;

namespace Argoscope.UnitTests;

public sealed class InMemoryAlertRuleStore : IAlertRuleStore
{
    private readonly Dictionary<Id<AlertRule>, AlertRule> _byId = new();
    private readonly object _lock = new();

    public Task<AlertRule?> FindAsync(Id<AlertRule> id, CancellationToken cancellationToken)
    {
        lock (_lock) { return Task.FromResult(_byId.TryGetValue(id, out var v) ? v : null); }
    }

    public Task<IReadOnlyList<AlertRule>> ListByPortfolioAsync(Id<Portfolio> portfolioId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<AlertRule>>(
                _byId.Values.Where(r => r.PortfolioId == portfolioId).OrderBy(r => r.CreatedAtUtc).ToList());
        }
    }

    public Task<AlertRule> AddAsync(AlertRule rule, CancellationToken cancellationToken)
    {
        lock (_lock) { _byId[rule.Id] = rule; }
        return Task.FromResult(rule);
    }

    public Task UpdateAsync(AlertRule rule, CancellationToken cancellationToken)
    {
        lock (_lock) { _byId[rule.Id] = rule; }
        return Task.CompletedTask;
    }
}

public sealed class InMemoryAlertEvaluationStore : IAlertEvaluationStore
{
    private readonly List<AlertEvaluation> _items = new();
    private readonly object _lock = new();

    public Task<AlertEvaluation?> FindAsync(Id<AlertEvaluation> id, CancellationToken cancellationToken)
    {
        lock (_lock) { return Task.FromResult(_items.FirstOrDefault(e => e.Id == id)); }
    }

    public Task<AlertEvaluation?> FindByKeyAsync(Id<AlertRule> ruleId, DateTimeOffset metricWindowEndUtc, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult(_items.FirstOrDefault(e =>
                e.RuleId == ruleId && e.MetricWindowEndUtc == metricWindowEndUtc));
        }
    }

    public Task<AlertEvaluation?> FindLatestFiredAsync(Id<AlertRule> ruleId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult(_items
                .Where(e => e.RuleId == ruleId && e.Status == AlertEvaluationStatus.Fired)
                .OrderByDescending(e => e.EvaluatedAtUtc)
                .FirstOrDefault());
        }
    }

    public Task<IReadOnlyList<AlertEvaluation>> ListByPortfolioAsync(Id<Portfolio> portfolioId, int limit, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<AlertEvaluation>>(
                _items.Where(e => e.PortfolioId == portfolioId)
                    .OrderByDescending(e => e.EvaluatedAtUtc)
                    .Take(Math.Clamp(limit, 1, 200))
                    .ToList());
        }
    }

    public Task<IReadOnlyList<AlertEvaluation>> ListByRuleAsync(Id<AlertRule> ruleId, int limit, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<AlertEvaluation>>(
                _items.Where(e => e.RuleId == ruleId)
                    .OrderByDescending(e => e.EvaluatedAtUtc)
                    .Take(Math.Clamp(limit, 1, 200))
                    .ToList());
        }
    }

    public Task<AlertEvaluation> AddAsync(AlertEvaluation evaluation, CancellationToken cancellationToken)
    {
        lock (_lock) { _items.Add(evaluation); }
        return Task.FromResult(evaluation);
    }
}

public sealed class InMemoryDeliveryAttemptStore : IDeliveryAttemptStore
{
    private readonly List<DeliveryAttempt> _items = new();
    private readonly object _lock = new();

    public Task<IReadOnlyList<DeliveryAttempt>> ListByEvaluationAsync(Id<AlertEvaluation> evaluationId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<DeliveryAttempt>>(
                _items.Where(a => a.EvaluationId == evaluationId).OrderBy(a => a.AttemptNumber).ToList());
        }
    }

    public Task<IReadOnlyList<DeliveryAttempt>> ListByPortfolioAsync(Id<Portfolio> portfolioId, int limit, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<DeliveryAttempt>>(
                _items.OrderByDescending(a => a.CreatedAtUtc).Take(Math.Clamp(limit, 1, 200)).ToList());
        }
    }

    public Task<DeliveryAttempt> AddAsync(DeliveryAttempt attempt, CancellationToken cancellationToken)
    {
        lock (_lock) { _items.Add(attempt); }
        return Task.FromResult(attempt);
    }

    public Task UpdateAsync(DeliveryAttempt attempt, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var index = _items.FindIndex(a => a.Id == attempt.Id);
            if (index >= 0)
            {
                _items[index] = attempt;
            }
        }
        return Task.CompletedTask;
    }
}
