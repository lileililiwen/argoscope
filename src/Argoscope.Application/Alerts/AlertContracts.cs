using Argoscope.Domain.Alerts;
using Argoscope.Domain.Common;
using Argoscope.Domain.Portfolios;
using Argoscope.Domain.Repositories;

namespace Argoscope.Application.Alerts;

public sealed record CreateAlertRuleCommand(
    Id<Portfolio> PortfolioId,
    Id<Repository>? RepositoryId,
    string Name,
    string MetricKey,
    string Operator,
    double Threshold,
    double MinimumCoverage,
    int CooldownHours,
    bool Enabled,
    string Channel,
    string Destination,
    string? Secret,
    DateTimeOffset Now);

public sealed record UpdateAlertRuleCommand(
    Id<Portfolio> PortfolioId,
    Id<AlertRule> RuleId,
    int ExpectedVersion,
    string Name,
    string MetricKey,
    string Operator,
    double Threshold,
    double MinimumCoverage,
    int CooldownHours,
    bool Enabled,
    string Channel,
    string Destination,
    string? Secret,
    DateTimeOffset Now);

public sealed record AlertRuleDto(
    Guid RuleId,
    Guid PortfolioId,
    Guid? RepositoryId,
    string Name,
    string MetricKey,
    string Operator,
    double Threshold,
    double MinimumCoverage,
    int CooldownHours,
    bool Enabled,
    string Channel,
    string DestinationMasked,
    bool HasSecret,
    int Version,
    DateTimeOffset? DeletedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record AlertEvaluationDto(
    Guid EvaluationId,
    Guid RuleId,
    string RuleName,
    Guid PortfolioId,
    Guid? RepositoryId,
    string MetricKey,
    DateTimeOffset MetricWindowEndUtc,
    double? MetricValue,
    double Coverage,
    string Status,
    string? Reason,
    DateTimeOffset EvaluatedAtUtc,
    IReadOnlyList<DeliveryAttemptDto> Attempts);

public sealed record DeliveryAttemptDto(
    Guid AttemptId,
    int AttemptNumber,
    string State,
    int? ResponseCode,
    string? Error,
    DateTimeOffset? NextRetryAtUtc,
    DateTimeOffset CreatedAtUtc);
