using Argoscope.Application.Alerts;
using Argoscope.Domain.Alerts;
using Argoscope.Domain.Common;
using Argoscope.Domain.Portfolios;
using Microsoft.EntityFrameworkCore;

namespace Argoscope.Infrastructure.Persistence;

public sealed class EfAlertRuleStore : IAlertRuleStore
{
    private readonly ArgoscopeDbContext _db;
    public EfAlertRuleStore(ArgoscopeDbContext db) => _db = db;

    public Task<AlertRule?> FindAsync(Id<AlertRule> id, CancellationToken cancellationToken) =>
        _db.AlertRules.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<AlertRule>> ListByPortfolioAsync(Id<Portfolio> portfolioId, CancellationToken cancellationToken) =>
        await _db.AlertRules
            .Where(r => r.PortfolioId == portfolioId)
            .OrderBy(r => r.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<AlertRule> AddAsync(AlertRule rule, CancellationToken cancellationToken)
    {
        await _db.AlertRules.AddAsync(rule, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return rule;
    }

    public async Task UpdateAsync(AlertRule rule, CancellationToken cancellationToken)
    {
        _db.AlertRules.Update(rule);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class EfAlertEvaluationStore : IAlertEvaluationStore
{
    private readonly ArgoscopeDbContext _db;
    public EfAlertEvaluationStore(ArgoscopeDbContext db) => _db = db;

    public Task<AlertEvaluation?> FindAsync(Id<AlertEvaluation> id, CancellationToken cancellationToken) =>
        _db.AlertEvaluations.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public Task<AlertEvaluation?> FindByKeyAsync(Id<AlertRule> ruleId, DateTimeOffset metricWindowEndUtc, CancellationToken cancellationToken) =>
        _db.AlertEvaluations.FirstOrDefaultAsync(
            e => e.RuleId == ruleId && e.MetricWindowEndUtc == metricWindowEndUtc, cancellationToken);

    public Task<AlertEvaluation?> FindLatestFiredAsync(Id<AlertRule> ruleId, CancellationToken cancellationToken) =>
        _db.AlertEvaluations
            .Where(e => e.RuleId == ruleId && e.Status == AlertEvaluationStatus.Fired)
            .OrderByDescending(e => e.EvaluatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<AlertEvaluation>> ListByPortfolioAsync(Id<Portfolio> portfolioId, int limit, CancellationToken cancellationToken) =>
        await _db.AlertEvaluations
            .Where(e => e.PortfolioId == portfolioId)
            .OrderByDescending(e => e.EvaluatedAtUtc)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<AlertEvaluation>> ListByRuleAsync(Id<AlertRule> ruleId, int limit, CancellationToken cancellationToken) =>
        await _db.AlertEvaluations
            .Where(e => e.RuleId == ruleId)
            .OrderByDescending(e => e.EvaluatedAtUtc)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<AlertEvaluation> AddAsync(AlertEvaluation evaluation, CancellationToken cancellationToken)
    {
        await _db.AlertEvaluations.AddAsync(evaluation, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return evaluation;
    }
}

public sealed class EfDeliveryAttemptStore : IDeliveryAttemptStore
{
    private readonly ArgoscopeDbContext _db;
    public EfDeliveryAttemptStore(ArgoscopeDbContext db) => _db = db;

    public async Task<IReadOnlyList<DeliveryAttempt>> ListByEvaluationAsync(Id<AlertEvaluation> evaluationId, CancellationToken cancellationToken) =>
        await _db.DeliveryAttempts
            .Where(a => a.EvaluationId == evaluationId)
            .OrderBy(a => a.AttemptNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<DeliveryAttempt>> ListByPortfolioAsync(Id<Portfolio> portfolioId, int limit, CancellationToken cancellationToken)
    {
        var evaluationIds = await _db.AlertEvaluations
            .Where(e => e.PortfolioId == portfolioId)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (evaluationIds.Count == 0)
        {
            return Array.Empty<DeliveryAttempt>();
        }
        return await _db.DeliveryAttempts
            .Where(a => evaluationIds.Contains(a.EvaluationId))
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<DeliveryAttempt> AddAsync(DeliveryAttempt attempt, CancellationToken cancellationToken)
    {
        await _db.DeliveryAttempts.AddAsync(attempt, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return attempt;
    }

    public async Task UpdateAsync(DeliveryAttempt attempt, CancellationToken cancellationToken)
    {
        _db.DeliveryAttempts.Update(attempt);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
