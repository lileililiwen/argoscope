using Argoscope.Domain.Common;
using Argoscope.Domain.Portfolios;
using Argoscope.Domain.Repositories;

using System.Text.Json.Serialization;

namespace Argoscope.Domain.Memberships;

/// <summary>
/// Owner-controlled lifecycle label. Argoscope surfaces the label but never
/// changes it from analytics; the owner is the only writer.
/// </summary>
public static class LifecycleStage
{
    public const string Idea = "Idea";
    public const string Prototype = "Prototype";
    public const string OpenSource = "OpenSource";
    public const string Growing = "Growing";
    public const string Validated = "Validated";
    public const string SaasCandidate = "SaaSCandidate";
    public const string Hosted = "Hosted";
    public const string Maintenance = "Maintenance";
    public const string Archived = "Archived";

    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
    {
        Idea, Prototype, OpenSource, Growing, Validated, SaasCandidate, Hosted, Maintenance, Archived,
    };

    public static bool IsAllowed(string? value) =>
        value is not null && Allowed.Contains(value);
}

/// <summary>Membership role: this repository is owned by the Argoscope operator or a competitor.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MembershipRole
{
    Owned = 0,
    Competitor = 1,
}

/// <summary>
/// Association between a <see cref="Portfolios.Portfolio"/> and a
/// <see cref="Repositories.Repository"/>. Argoscope keeps one membership per
/// (Portfolio, Repository). Renaming the repository or changing role/lifecycle
/// is audited by the infrastructure layer; the membership identity is stable.
/// </summary>
public sealed class PortfolioRepository : Entity<Id<PortfolioRepository>>
{
    public Id<Portfolio> PortfolioId { get; private set; }

    public Id<Repository> RepositoryId { get; private set; }

    public MembershipRole Role { get; private set; }

    public string? Category { get; private set; }

    public string Lifecycle { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private PortfolioRepository() : base() { }

    public PortfolioRepository(
        Id<Portfolio> portfolioId,
        Id<Repository> repositoryId,
        MembershipRole role,
        string? category,
        string lifecycle,
        DateTimeOffset now)
        : base(Id<PortfolioRepository>.New())
    {
        if (string.IsNullOrWhiteSpace(lifecycle) || !LifecycleStage.IsAllowed(lifecycle))
        {
            throw new DomainException("validation", $"Lifecycle must be one of: {string.Join(", ", LifecycleStage.Allowed)}.");
        }

        PortfolioId = portfolioId;
        RepositoryId = repositoryId;
        Role = role;
        Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        Lifecycle = lifecycle;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
    }

    public void Update(string? category, string lifecycle, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(lifecycle) || !LifecycleStage.IsAllowed(lifecycle))
        {
            throw new DomainException("validation", $"Lifecycle must be one of: {string.Join(", ", LifecycleStage.Allowed)}.");
        }

        Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        Lifecycle = lifecycle;
        UpdatedAtUtc = now;
    }
}
