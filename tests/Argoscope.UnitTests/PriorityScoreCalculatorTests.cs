using Argoscope.Application.Ranking;
using Argoscope.Domain.Common;
using Argoscope.Domain.Portfolios;
using Argoscope.Domain.Scores;
using Xunit;
namespace Argoscope.UnitTests;

public class PriorityScoreCalculatorTests
{
    private static ScoreFactor F(string name, double w, bool enabled = true) => new(name, w, enabled);

    [Fact]
    public void ValidateRejectsNegativeOrInfiniteOrAboveOneWeights()
    {
        Assert.Throws<DomainException>(() => F(FactorNames.Momentum, -0.1d));
        Assert.Throws<DomainException>(() => F(FactorNames.Momentum, double.NaN));
        Assert.Throws<DomainException>(() => F(FactorNames.Momentum, double.PositiveInfinity));
        Assert.Throws<DomainException>(() => F(FactorNames.Momentum, 1.5d));
    }

    [Fact]
    public void ValidateRejectsEmptyFactorList()
    {
        Assert.True(PriorityScoreCalculator.Validate(Array.Empty<ScoreFactor>()).IsFailure);
    }

    [Fact]
    public void ValidateRejectsAllZeroOrAllDisabledWeights()
    {
        Assert.True(PriorityScoreCalculator.Validate(new[] { F(FactorNames.Momentum, 0d, enabled: false) }).IsFailure);
        Assert.True(PriorityScoreCalculator.Validate(new[] { F(FactorNames.Momentum, 0d) }).IsFailure);
    }

    [Fact]
    public void ComputeReturnsZeroTo100ScoresWithEffectiveWeightsAndMissingFactors()
    {
        var portfolioId = Id<Portfolio>.New();
        var factors = new List<ScoreFactor>
        {
            F(FactorNames.Momentum, 0.30d),
            F(FactorNames.Engagement, 0.25d),
            F(FactorNames.Adoption, 0.20d, enabled: false),
            F(FactorNames.ExternalUsers, 0.15d, enabled: false),
            F(FactorNames.Commercial, 0.10d, enabled: false),
        };
        var config = new ScoreConfiguration(portfolioId, 1, factors, default);
        var inputs = new[]
        {
            new RepositoryFactors("a", "owner/repo-a",
                new Dictionary<string, double?>
                {
                    [FactorNames.Momentum] = 50d,
                    [FactorNames.Engagement] = 25d,
                },
                new Dictionary<string, string?>
                {
                    [FactorNames.Adoption] = "adoption-not-implemented",
                    [FactorNames.ExternalUsers] = "external-not-implemented",
                    [FactorNames.Commercial] = "commercial-not-implemented",
                }),
        };
        var run = PriorityScoreCalculator.Compute(config, inputs, default);
        Assert.Single(run.Ranking);
        var row = run.RunningRankingOrDefault();
        Assert.Equal(2, row!.EffectiveFactorCount);
        Assert.Contains(FactorNames.Momentum, row.EffectiveWeights.Keys);
        Assert.Contains(FactorNames.Engagement, row.EffectiveWeights.Keys);
        Assert.DoesNotContain(FactorNames.Adoption, row.EffectiveWeights.Keys);
        Assert.Null(row.FactorValues[FactorNames.Adoption]);
        Assert.Equal(0.4, row.Coverage, 6);
    }
}

internal static class PriorityScoreRunExtensions
{
    public static PriorityScore? RunningRankingOrDefault(this PriorityScoreRun run) => run.Ranking.FirstOrDefault();
}
