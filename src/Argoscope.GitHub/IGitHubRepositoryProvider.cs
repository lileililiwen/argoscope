using Argoscope.Domain.Common;
using Argoscope.Domain.Repositories;
using Argoscope.Domain.Snapshots;

namespace Argoscope.GitHub;

/// <summary>
/// One row of normalized GitHub data for a single metric on a single day.
/// The provider returns a list of observations per call; the collection
/// service turns each into a <see cref="Domain.Snapshots.MetricSnapshot"/>.
/// </summary>
/// <param name="MetricName">A value from <see cref="Domain.Snapshots.MetricNames"/>.</param>
/// <param name="MetricDate">The day the value applies to (UTC).</param>
/// <param name="Value">Numeric counter, or null if the provider could not authorize the field.</param>
/// <param name="ObservedAtUtc">Source-side timestamp (e.g. created_at, updated_at) when available.</param>
public sealed record MetricObservation(
    string MetricName,
    DateOnly MetricDate,
    double? Value,
    DateTimeOffset ObservedAtUtc);

/// <summary>
/// Engagement activity for a single day, split by attribution so the
/// application layer never has to interpret author identity itself.
/// </summary>
public sealed record EngagementObservation(
    DateOnly BucketDate,
    int ExternalIssuesOpened,
    int ExternalPullRequestsOpened,
    int ExternalContributors,
    int OwnerIssuesOpened,
    int OwnerPullRequestsOpened,
    int OwnerContributors,
    int UnknownIssuesOpened,
    int UnknownPullRequestsOpened,
    int UnknownContributors,
    DateTimeOffset ObservedAtUtc);

/// <summary>Repository-level metadata from the provider (identity + visibility + dates + language).</summary>
public sealed record RepositoryObservation(
    string NodeId,
    string OwnerLogin,
    string Name,
    RepositoryVisibility Visibility,
    DateOnly? CreatedOnGithubAt,
    string? PrimaryLanguage,
    DateTimeOffset? LastActivityAtUtc,
    DateTimeOffset ObservedAtUtc);

/// <summary>One page of a paginated provider call plus the next-page cursor and the result status.</summary>
public sealed record ProviderPage<T>(
    IReadOnlyList<T> Items,
    string? NextCursor,
    ProviderResultStatus Status,
    DateTimeOffset? RetryAfterUtc,
    string? DiagnosticCode);

/// <summary>
/// Provider-agnostic access to GitHub repository data. The collection
/// service depends on this interface only; the real REST/GraphQL adapter
/// and the in-memory fake both implement it.
/// </summary>
public interface IGitHubRepositoryProvider
{
    /// <summary>Stable version string for the response shape; stored on every snapshot.</summary>
    string ProviderVersion { get; }

    /// <summary>Look up the canonical repository observation (identity + visibility + dates).</summary>
    Task<ProviderResult<RepositoryObservation>> GetRepositoryAsync(
        string ownerLogin, string name, CancellationToken cancellationToken);

    /// <summary>Page through metric observations for one repository; respects the supplied cursor.</summary>
    Task<ProviderPage<MetricObservation>> GetMetricPageAsync(
        string ownerLogin, string name, string metricName, string? cursor, DateOnly sinceDate, CancellationToken cancellationToken);

    /// <summary>Page through engagement observations for one repository.</summary>
    Task<ProviderPage<EngagementObservation>> GetEngagementPageAsync(
        string ownerLogin, string name, string? cursor, DateTimeOffset sinceUtc, CancellationToken cancellationToken);
}

/// <summary>Single-value provider result; the collection service translates this into snapshots.</summary>
public readonly struct ProviderResult<T>
{
    public T? Value { get; }

    public ProviderResultStatus Status { get; }

    public DateTimeOffset? RetryAfterUtc { get; }

    public string? DiagnosticCode { get; }

    private ProviderResult(T? value, ProviderResultStatus status, DateTimeOffset? retryAfterUtc, string? diagnosticCode)
    {
        Value = value;
        Status = status;
        RetryAfterUtc = retryAfterUtc;
        DiagnosticCode = diagnosticCode;
    }

    public static ProviderResult<T> Ok(T value) => new(value, ProviderResultStatus.Available, null, null);

    public static ProviderResult<T> Partial(T value, string code) => new(value, ProviderResultStatus.Partial, null, code);

    public static ProviderResult<T> Failure(ProviderResultStatus status, string? code, DateTimeOffset? retryAfter = null) =>
        new(default, status, retryAfter, code);
}
