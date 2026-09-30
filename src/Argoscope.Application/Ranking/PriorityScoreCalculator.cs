using Argoscope.Domain.Common;
using Argoscope.Domain.Scores;

namespace Argoscope.Application.Ranking;

/// <summary>Per-repository values for each configured factor. Missing values are null.</summary>
public sealed record FactorInputs(
    IReadOnlyDictionary<string, double?> Values,
    IReadOnlyDictionary<string, string?> MissingReasons);

/// <summary>One repository's priority score output, including provenance and effective weights.</summary>
public sealed record PriorityScore(
    string RepositoryId,
    string RepositoryLabel,
    double Score,
    int EffectiveFactorCount,
    IReadOnlyDictionary<string, double> EffectiveWeights,
    IReadOnlyDictionary<string, double?> FactorValues,
    IReadOnlyDictionary<string, string?> MissingFactors,
    double Coverage,
    string ScoreConfigurationVersion,
    DateTimeOffset AsOfUtc);

/// <summary>Full score run output for a portfolio: ranking plus the input factors.</summary>
public sealed record PriorityScoreRun(
    string PortfolioId,
    string ScoreConfigurationVersion,
    IReadOnlyList<PriorityScore> Ranking,
    DateTimeOffset AsOfUtc,
    string? InsufficientReason);

/// <summary>
/// Pure priority score computation. The factor values for a repository are
/// normalized by dividing each into the same metric's portfolio maximum; the
/// MVP does not require a specific normalization formula beyond being
/// deterministic and bounded, and the design record documents the choice.
/// Missing factors are excluded from both numerator and denominator; effective
/// weights are renormalized; the score is 100 * sum(weight * normalized_value)
/// / sum(weights). Coverage is the share of configured factors that produced a
/// value for the repository.
/// </summary>
public static class PriorityScoreCalculator
{
    public static PriorityScoreRun Compute(
        ScoreConfiguration config,
        IReadOnlyList<RepositoryFactors> factors,
        DateTimeOffset asOfUtc)
    {
        // Build the active factor list (configured + enabled + has at least one value somewhere).
        var activeNames = config.Factors.Where(f => f.Enabled && f.Weight > 0d).Select(f => f.Name).ToHashSet();
        if (activeNames.Count == 0)
        {
            return new PriorityScoreRun(config.PortfolioId.Value.ToString(), config.Version.ToString(), Array.Empty<PriorityScore>(), asOfUtc,
                "no-active-factors");
        }

        // Compute per-factor max (over repositories that have a value) for normalization.
        var perFactorMax = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var name in activeNames)
        {
            var values = factors
                .Select(r => r.FactorValues.TryGetValue(name, out var v) ? v : null)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();
            if (values.Count == 0)
            {
                perFactorMax[name] = 0d;
            }
            else
            {
                // Use a symmetric reference: max(|values|) so positive and negative factors compare.
                perFactorMax[name] = values.Max(v => Math.Abs(v));
            }
        }

        var ranking = new List<PriorityScore>(factors.Count);
        foreach (var repo in factors)
        {
            var effective = new Dictionary<string, double>(StringComparer.Ordinal);
            var values = new Dictionary<string, double?>(StringComparer.Ordinal);
            var missing = new Dictionary<string, string?>(StringComparer.Ordinal);
            double numerator = 0d;
            double denominator = 0d;
            int effectiveCount = 0;

            foreach (var factor in config.Factors)
            {
                repo.FactorValues.TryGetValue(factor.Name, out var value);
                repo.MissingReasons.TryGetValue(factor.Name, out var reason);
                if (activeNames.Contains(factor.Name) && value.HasValue && perFactorMax[factor.Name] > 0d)
                {
                    var normalized = value.Value / perFactorMax[factor.Name];
                    // Map [-1, 1] to [0, 1].
                    var shifted = (normalized + 1d) / 2d;
                    numerator += factor.Weight * shifted;
                    denominator += factor.Weight;
                    effective[factor.Name] = factor.Weight;
                    values[factor.Name] = value;
                    missing[factor.Name] = null;
                    effectiveCount++;
                }
                else
                {
                    values[factor.Name] = value;
                    missing[factor.Name] = reason ?? "no-value";
                }
            }

            var score = denominator > 0d ? Math.Round(100d * numerator / denominator, 4) : 0d;
            var coverage = config.Factors.Count == 0 ? 0d : (double)effectiveCount / config.Factors.Count;
            ranking.Add(new PriorityScore(
                repo.RepositoryId,
                repo.RepositoryLabel,
                score,
                effectiveCount,
                effective,
                values,
                missing,
                coverage,
                config.Version.ToString(),
                asOfUtc));
        }

        var ordered = ranking.OrderByDescending(r => r.Score).ThenBy(r => r.RepositoryLabel, StringComparer.Ordinal).ToList();
        return new PriorityScoreRun(config.PortfolioId.Value.ToString(), config.Version.ToString(), ordered, asOfUtc, null);
    }

    /// <summary>Validate an in-memory list of factors: sum of enabled weights must be > 0; weights finite, non-negative, &lt;= 1; names from the closed set.</summary>
    public static Result Validate(IEnumerable<ScoreFactor> factors)
    {
        var list = factors.ToList();
        if (list.Count == 0)
        {
            return Error.Validation("At least one factor is required.");
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in list)
        {
            if (string.IsNullOrWhiteSpace(f.Name) || !FactorNames.All.Contains(f.Name))
            {
                return Error.Validation($"Factor name must be one of: {string.Join(", ", FactorNames.All)}.");
            }
            if (!names.Add(f.Name))
            {
                return Error.Validation($"Duplicate factor {f.Name}.");
            }
            if (double.IsNaN(f.Weight) || double.IsInfinity(f.Weight) || f.Weight < 0d)
            {
                return Error.Validation("Weight must be a non-negative finite number.");
            }
            if (f.Weight > 1d)
            {
                return Error.Validation("Weight must not exceed 1.0.");
            }
        }
        var total = list.Where(f => f.Enabled).Sum(f => f.Weight);
        if (total <= 0d)
        {
            return Error.Validation("Sum of enabled weights must be greater than zero.");
        }
        return Result.Success();
    }
}

/// <summary>Per-repository factor inputs supplied by the read model.</summary>
public sealed record RepositoryFactors(
    string RepositoryId,
    string RepositoryLabel,
    IReadOnlyDictionary<string, double?> FactorValues,
    IReadOnlyDictionary<string, string?> MissingReasons);
