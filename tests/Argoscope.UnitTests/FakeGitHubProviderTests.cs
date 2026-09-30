using Argoscope.Domain.Memberships;
using Argoscope.Domain.Repositories;
using Argoscope.Domain.Snapshots;
using Argoscope.GitHub;
using Xunit;

namespace Argoscope.UnitTests;

public class FakeGitHubProviderTests
{
    [Fact]
    public async Task Fake_provider_returns_configured_repository_observation()
    {
        var fake = new FakeGitHubRepositoryProvider();
        fake.AddRepository(new FakeGitHubRepositoryProvider.RepositoryFixture(
            "octo", "hello", "node-1", RepositoryVisibility.Public, new DateOnly(2020, 1, 1), "C#", default));
        var result = await fake.GetRepositoryAsync("octo", "hello", default);
        Assert.Equal(ProviderResultStatus.Available, result.Status);
        Assert.NotNull(result.Value);
        Assert.Equal("node-1", result.Value!.NodeId);
    }

    [Fact]
    public async Task Fake_provider_returns_not_found_for_unknown_repo()
    {
        var fake = new FakeGitHubRepositoryProvider();
        var result = await fake.GetRepositoryAsync("octo", "nope", default);
        Assert.Equal(ProviderResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task Fake_provider_paginates_metrics_by_configured_page_size()
    {
        var fake = new FakeGitHubRepositoryProvider();
        for (var d = 1; d <= 12; d++)
        {
            fake.AddMetric(new FakeGitHubRepositoryProvider.MetricFixture("octo", "hello", "stars", new DateOnly(2024, 1, d), d));
        }
        fake.MetricPageSizes[("octo", "hello", "stars")] = 5;
        var page1 = await fake.GetMetricPageAsync("octo", "hello", "stars", null, new DateOnly(2024, 1, 1), default);
        var page2 = await fake.GetMetricPageAsync("octo", "hello", "stars", page1.NextCursor, new DateOnly(2024, 1, 1), default);
        var page3 = await fake.GetMetricPageAsync("octo", "hello", "stars", page2.NextCursor, new DateOnly(2024, 1, 1), default);
        Assert.Equal(5, page1.Items.Count);
        Assert.Equal(5, page2.Items.Count);
        Assert.Equal(2, page3.Items.Count);
        Assert.Null(page3.NextCursor);
    }

    [Fact]
    public async Task Fake_provider_records_every_call()
    {
        var fake = new FakeGitHubRepositoryProvider();
        fake.AddRepository(new FakeGitHubRepositoryProvider.RepositoryFixture("octo", "hello", "node-1", RepositoryVisibility.Public, null, null, null));
        fake.AddMetric(new FakeGitHubRepositoryProvider.MetricFixture("octo", "hello", "stars", new DateOnly(2024, 1, 1), 1));
        _ = await fake.GetRepositoryAsync("octo", "hello", default);
        _ = await fake.GetMetricPageAsync("octo", "hello", "stars", null, new DateOnly(2024, 1, 1), default);
        Assert.Single(fake.RepositoryCalls);
        Assert.Single(fake.MetricCalls);
    }

    [Fact]
    public void Repository_rejects_blank_node_id()
    {
        Assert.Throws<Argoscope.Domain.Common.DomainException>(() =>
            new Repository(" ", "octo", "hello", RepositoryVisibility.Public, default));
    }
}
