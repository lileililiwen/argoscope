using Argoscope.Domain.Common;
using Argoscope.Domain.Portfolios;

namespace Argoscope.Domain.Scores;

/// <summary>
/// Stable ids for the priority factors the MVP score understands. The factor
/// list is fixed; the weights and enabled flags are configurable per
/// portfolio. Missing values are not silently replaced with zero.
/// </summary>
public static class FactorNames
{
    public const string Momentum = "momentum";
    public const string Engagement = "engagement";
    public const string Adoption = "adoption";
    public const string ExternalUsers = "external_users";
    public const string Commercial = "commercial";

    /// <summary>Default brief weights: momentum .30, engagement .25, adoption .20, external .15, commercial .10.</summary>
    public static readonly IReadOnlyDictionary<string, double> BriefDefaults =
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [Momentum] = 0.30d,
            [Engagement] = 0.25d,
            [Adoption] = 0.20d,
            [ExternalUsers] = 0.15d,
            [Commercial] = 0.10d,
        };

    public static readonly IReadOnlyList<string> All = new[]
    {
        Momentum, Engagement, Adoption, ExternalUsers, Commercial,
    };
}

/// <summary>One factor's weight configuration in a portfolio's score setup.</summary>
public sealed class ScoreFactor
{
    /// <summary>EF Core parameterless constructor.</summary>
    private ScoreFactor() { Name = string.Empty; Weight = 0d; Enabled = false; }

    public string Name { get; private set; }
    public double Weight { get; private set; }
    public bool Enabled { get; private set; }

    public ScoreFactor(string name, double weight, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(name) || !FactorNames.All.Contains(name))
        {
            throw new DomainException("validation", $"Factor name must be one of: {string.Join(", ", FactorNames.All)}.");
        }
        if (double.IsNaN(weight) || double.IsInfinity(weight) || weight < 0d)
        {
            throw new DomainException("validation", "Weight must be a non-negative finite number.");
        }
        if (weight > 1d)
        {
            throw new DomainException("validation", "Weight must not exceed 1.0.");
        }

        Name = name.Trim();
        Weight = weight;
        Enabled = enabled;
    }
}

/// <summary>
/// Versioned configuration of the priority score for a portfolio. Sum of
/// enabled weights must be > 0; all-zero or all-disabled is rejected.
/// </summary>
public sealed class ScoreConfiguration : Entity<Id<ScoreConfiguration>>
{
    public Id<Portfolio> PortfolioId { get; private set; }

    public int Version { get; private set; }

    public IReadOnlyList<ScoreFactor> Factors => _factors;

    private readonly List<ScoreFactor> _factors;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private ScoreConfiguration() { _factors = new List<ScoreFactor>(); }

    public ScoreConfiguration(Id<Portfolio> portfolioId, int version, IEnumerable<ScoreFactor> factors, DateTimeOffset now)
        : base(Id<ScoreConfiguration>.New())
    {
        _factors = factors.ToList();
        if (_factors.Count == 0)
        {
            throw new DomainException("validation", "At least one factor is required.");
        }
        var total = _factors.Where(f => f.Enabled).Sum(f => f.Weight);
        if (total <= 0d)
        {
            throw new DomainException("validation", "Sum of enabled weights must be greater than zero.");
        }

        PortfolioId = portfolioId;
        Version = version;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
    }
}
