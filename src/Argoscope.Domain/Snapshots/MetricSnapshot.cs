using Argoscope.Domain.Common;
using Argoscope.Domain.Repositories;

namespace Argoscope.Domain.Snapshots;

/// <summary>
/// Stable names for the daily metrics Argoscope tracks. A daily snapshot is one
/// row per (RepositoryId, MetricName, MetricDate, ProviderVersion) tuple;
/// collection is idempotent.
/// </summary>
public static class MetricNames
{
    public const string Stars = "stars";
    public const string Forks = "forks";
    public const string Watchers = "watchers";
    public const string OpenIssues = "open_issues";
    public const string OpenPullRequests = "open_pull_requests";
    public const string Contributors = "contributors";
    public const string ReleasesLastYear = "releases_last_year";
    public const string CommitsLast30Days = "commits_last_30_days";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Stars, Forks, Watchers, OpenIssues, OpenPullRequests, Contributors, ReleasesLastYear, CommitsLast30Days,
    };
}

/// <summary>Classification of the provider result that produced this snapshot.</summary>
public enum ProviderResultStatus
{
    Available = 0,
    Partial = 1,
    Stale = 2,
    RateLimited = 3,
    Unauthorized = 4,
    Forbidden = 5,
    NotFound = 6,
    Unavailable = 7,
    Malformed = 8,
}

/// <summary>
/// One metric point for a single repository on a single day. Idempotent: a
/// second successful same-day write is allowed to overwrite the value but must
/// not be allowed to silently replace a previously verified value with a
/// missing one (the application layer enforces that invariant).
/// </summary>
public sealed class MetricSnapshot : Entity<Id<MetricSnapshot>>
{
    public Id<Repository> RepositoryId { get; private set; }

    public string MetricName { get; private set; }

    public DateOnly MetricDate { get; private set; }

    public string ProviderVersion { get; private set; }

    public double? Value { get; private set; }

    public ProviderResultStatus Status { get; private set; }

    public bool IsComplete { get; private set; }

    public DateTimeOffset ObservedAtUtc { get; private set; }

    public DateTimeOffset CollectedAtUtc { get; private set; }

    public string? DiagnosticCode { get; private set; }

    private MetricSnapshot() : base() { }

    public MetricSnapshot(
        Id<Repository> repositoryId,
        string metricName,
        DateOnly metricDate,
        string providerVersion,
        double? value,
        ProviderResultStatus status,
        bool isComplete,
        DateTimeOffset observedAtUtc,
        DateTimeOffset collectedAtUtc,
        string? diagnosticCode = null)
        : base(Id<MetricSnapshot>.New())
    {
        if (string.IsNullOrWhiteSpace(metricName))
        {
            throw new DomainException("validation", "Metric name is required.");
        }
        if (string.IsNullOrWhiteSpace(providerVersion))
        {
            throw new DomainException("validation", "Provider version is required.");
        }

        RepositoryId = repositoryId;
        MetricName = metricName.Trim();
        MetricDate = metricDate;
        ProviderVersion = providerVersion.Trim();
        Value = value;
        Status = status;
        IsComplete = isComplete;
        ObservedAtUtc = observedAtUtc;
        CollectedAtUtc = collectedAtUtc;
        DiagnosticCode = diagnosticCode;
    }
}
