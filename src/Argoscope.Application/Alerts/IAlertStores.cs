using Argoscope.Domain.Alerts;
using Argoscope.Domain.Common;
using Argoscope.Domain.Portfolios;
using Argoscope.Domain.Repositories;

namespace Argoscope.Application.Alerts;

/// <summary>Persistence contracts for alert rules, evaluations and attempts.</summary>
public interface IAlertRuleStore
{
    Task<AlertRule?> FindAsync(Id<AlertRule> id, CancellationToken cancellationToken);
    Task<IReadOnlyList<AlertRule>> ListByPortfolioAsync(Id<Portfolio> portfolioId, CancellationToken cancellationToken);
    Task<AlertRule> AddAsync(AlertRule rule, CancellationToken cancellationToken);
    Task UpdateAsync(AlertRule rule, CancellationToken cancellationToken);
}

public interface IAlertEvaluationStore
{
    Task<AlertEvaluation?> FindAsync(Id<AlertEvaluation> id, CancellationToken cancellationToken);
    Task<AlertEvaluation?> FindByKeyAsync(Id<AlertRule> ruleId, DateTimeOffset metricWindowEndUtc, CancellationToken cancellationToken);
    Task<AlertEvaluation?> FindLatestFiredAsync(Id<AlertRule> ruleId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AlertEvaluation>> ListByPortfolioAsync(Id<Portfolio> portfolioId, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<AlertEvaluation>> ListByRuleAsync(Id<AlertRule> ruleId, int limit, CancellationToken cancellationToken);
    Task<AlertEvaluation> AddAsync(AlertEvaluation evaluation, CancellationToken cancellationToken);
}

public interface IDeliveryAttemptStore
{
    Task<IReadOnlyList<DeliveryAttempt>> ListByEvaluationAsync(Id<AlertEvaluation> evaluationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<DeliveryAttempt>> ListByPortfolioAsync(Id<Portfolio> portfolioId, int limit, CancellationToken cancellationToken);
    Task<DeliveryAttempt> AddAsync(DeliveryAttempt attempt, CancellationToken cancellationToken);
    Task UpdateAsync(DeliveryAttempt attempt, CancellationToken cancellationToken);
}
