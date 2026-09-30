using Argoscope.Domain.Common;
using Argoscope.Domain.Portfolios;
using Argoscope.Domain.Repositories;

namespace Argoscope.Domain.Alerts;

/// <summary>
/// Per-portfolio alert rule over the allowlisted deterministic metric set.
/// Deletion soft-disables the rule and preserves evaluation history.
/// Channel secrets are write-only: the domain holds them but the read
/// API must only ever return masked values.
/// </summary>
public sealed class AlertRule : Entity<Id<AlertRule>>
{
    public const int MaxNameLength = 200;
    public const int MaxDestinationLength = 2000;
    public const int MaxSecretLength = 2000;

    public Id<Portfolio> PortfolioId { get; private set; }

    public Id<Repository>? RepositoryId { get; private set; }

    public string Name { get; private set; }

    public string MetricKey { get; private set; }

    public AlertOperator Operator { get; private set; }

    public double Threshold { get; private set; }

    public double MinimumCoverage { get; private set; }

    public int CooldownHours { get; private set; }

    public bool Enabled { get; private set; }

    public AlertChannel Channel { get; private set; }

    /// <summary>Email address or webhook URL. Never logged; masked on read.</summary>
    public string Destination { get; private set; }

    /// <summary>Webhook HMAC secret or empty for email rules. Write-only.</summary>
    public string Secret { get; private set; }

    public int Version { get; private set; }

    public DateTimeOffset? DeletedAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private AlertRule() : base() { }

    public AlertRule(
        Id<Portfolio> portfolioId,
        Id<Repository>? repositoryId,
        string name,
        string metricKey,
        AlertOperator @operator,
        double threshold,
        double minimumCoverage,
        int cooldownHours,
        bool enabled,
        AlertChannel channel,
        string destination,
        string secret,
        DateTimeOffset now)
        : base(Id<AlertRule>.New())
    {
        ValidateName(name);
        ValidateMetricKey(metricKey);
        ValidateOperator(@operator);
        ValidateCoverage(minimumCoverage);
        ValidateCooldown(cooldownHours);
        ValidateChannel(channel);
        ValidateDestination(channel, destination);
        ValidateSecret(secret);

        PortfolioId = portfolioId;
        RepositoryId = repositoryId;
        Name = name.Trim();
        MetricKey = metricKey;
        Operator = @operator;
        Threshold = threshold;
        MinimumCoverage = minimumCoverage;
        CooldownHours = cooldownHours;
        Enabled = enabled;
        Channel = channel;
        Destination = destination.Trim();
        Secret = secret ?? string.Empty;
        Version = 1;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
    }

    public void ApplyUpdate(
        string name,
        string metricKey,
        AlertOperator @operator,
        double threshold,
        double minimumCoverage,
        int cooldownHours,
        bool enabled,
        AlertChannel channel,
        string destination,
        string? secret,
        DateTimeOffset now)
    {
        if (DeletedAtUtc is not null)
        {
            throw new DomainException("conflict", "Alert rule has been deleted.");
        }
        ValidateName(name);
        ValidateMetricKey(metricKey);
        ValidateOperator(@operator);
        ValidateCoverage(minimumCoverage);
        ValidateCooldown(cooldownHours);
        ValidateChannel(channel);
        ValidateDestination(channel, destination);
        if (secret is not null)
        {
            ValidateSecret(secret);
        }

        Name = name.Trim();
        MetricKey = metricKey;
        Operator = @operator;
        Threshold = threshold;
        MinimumCoverage = minimumCoverage;
        CooldownHours = cooldownHours;
        Enabled = enabled;
        Channel = channel;
        Destination = destination.Trim();
        if (secret is not null)
        {
            Secret = secret;
        }
        Version += 1;
        UpdatedAtUtc = now;
    }

    /// <summary>Soft-disable the rule; history remains readable.</summary>
    public void SoftDelete(DateTimeOffset now)
    {
        if (DeletedAtUtc is not null)
        {
            throw new DomainException("conflict", "Alert rule is already deleted.");
        }
        DeletedAtUtc = now;
        Enabled = false;
        Version += 1;
        UpdatedAtUtc = now;
    }

    public bool IsDeleted => DeletedAtUtc is not null;

    public static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("validation", "Rule name is required.");
        }
        if (name.Length > MaxNameLength)
        {
            throw new DomainException("validation", $"Rule name must be {MaxNameLength} characters or fewer.");
        }
    }

    public static void ValidateMetricKey(string metricKey)
    {
        if (!AlertMetricAllowlist.IsSupported(metricKey))
        {
            throw new DomainException(
                "validation",
                $"MetricKey must be one of: {string.Join(", ", AlertMetricAllowlist.All)}.");
        }
    }

    public static void ValidateOperator(AlertOperator @operator)
    {
        if (@operator is AlertOperator.Unknown)
        {
            throw new DomainException("validation", "Operator must be GreaterThan, GreaterThanOrEqual, LessThan or LessThanOrEqual.");
        }
    }

    public static void ValidateCoverage(double coverage)
    {
        if (double.IsNaN(coverage) || double.IsInfinity(coverage) || coverage is < 0d or > 1d)
        {
            throw new DomainException("validation", "MinimumCoverage must be between 0 and 1.");
        }
    }

    public static void ValidateCooldown(int cooldownHours)
    {
        if (cooldownHours < 0 || cooldownHours > 24 * 30)
        {
            throw new DomainException("validation", "CooldownHours must be between 0 and 720.");
        }
    }

    public static void ValidateChannel(AlertChannel channel)
    {
        if (channel is AlertChannel.Unknown)
        {
            throw new DomainException("validation", "Channel must be Email or Webhook.");
        }
    }

    public static void ValidateDestination(AlertChannel channel, string destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
        {
            throw new DomainException("validation", "Destination is required.");
        }
        if (destination.Length > MaxDestinationLength)
        {
            throw new DomainException("validation", $"Destination must be {MaxDestinationLength} characters or fewer.");
        }
        if (channel == AlertChannel.Email)
        {
            var trimmed = destination.Trim();
            if (!trimmed.Contains('@') || trimmed.Contains(' ') || !trimmed.Contains('.'))
            {
                throw new DomainException("validation", "Destination must be a valid email address for Email rules.");
            }
        }
        if (channel == AlertChannel.Webhook)
        {
            var safety = WebhookSafety.CheckStatic(destination.Trim());
            if (!safety.IsAllowed)
            {
                throw new DomainException("validation", safety.Reason ?? "Webhook destination is not allowed.");
            }
        }
    }

    public static void ValidateSecret(string secret)
    {
        if (secret is not null && secret.Length > MaxSecretLength)
        {
            throw new DomainException("validation", $"Secret must be {MaxSecretLength} characters or fewer.");
        }
    }
}
