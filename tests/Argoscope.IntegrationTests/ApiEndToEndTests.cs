using System.Net;
using System.Net.Http.Json;
using Argoscope.Api;
using Argoscope.Application.Portfolios;
using Argoscope.Domain.Common;
using Argoscope.Domain.Memberships;
using Argoscope.Domain.Portfolios;
using Argoscope.Domain.Repositories;
using Argoscope.Infrastructure.Persistence;
using Argoscope.IntegrationTests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Argoscope.IntegrationTests;

[Collection("ApiFactory")]
public class ApiEndToEndTests
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiEndToEndTests(ApiFactory factory)
    {
        _factory = factory.Factory;
    }

    [Fact]
    public async Task CreatePortfolio_AddRepositories_GetOverview_Score_Benchmarks_Roundtrip()
    {
        using var client = _factory.CreateClient();
        // 1. Create portfolio.
        var createResp = await client.PostAsJsonAsync("/api/v1/portfolios", new { name = "Demo" });
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var portfolioJson = await createResp.Content.ReadAsStringAsync();
        var portfolio = System.Text.Json.JsonSerializer.Deserialize<PortfolioDto>(portfolioJson, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.NotNull(portfolio);

        // 2. Add 5 repositories in a category with a mix of stars so the median works.
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var resp = await client.PostAsJsonAsync($"/api/v1/portfolios/{portfolio!.Id}/repositories", new
            {
                nodeId = $"node-{i}",
                ownerLogin = "octo",
                name = $"repo-{i}",
                visibility = "Public",
                role = i == 0 ? "Owned" : "Competitor",
                category = i == 0 ? null : "ci",
                lifecycle = "OpenSource",
            });
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            var member = await resp.Content.ReadFromJsonAsync<MembershipDto>();
            ids.Add(member!.RepositoryId);
        }

        // 3. List memberships.
        var listResp = await client.GetAsync($"/api/v1/portfolios/{portfolio.Id}/repositories");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var memberships = await listResp.Content.ReadFromJsonAsync<List<MembershipDto>>();
        Assert.Equal(5, memberships!.Count);

        // 4. Run collection on each (fake provider returns nothing by default, so this exercises the failure paths).
        foreach (var id in ids)
        {
            var collect = await client.PostAsync($"/api/v1/portfolios/{portfolio.Id}/repositories/{id}/collect", content: null);
            Assert.Equal(HttpStatusCode.OK, collect.StatusCode);
        }

        // 5. Get overview (works with no snapshots, returns empty rows).
        var overview = await client.GetAsync($"/api/v1/portfolios/{portfolio.Id}/overview?window=30d");
        Assert.Equal(HttpStatusCode.OK, overview.StatusCode);
        var overviewBody = await overview.Content.ReadFromJsonAsync<OverviewEnvelope>();
        Assert.Equal(5, overviewBody!.Rows.Count);

        // 6. Get benchmarks (insufficient cohort coverage: 4 competitors have category=ci, 1 owned has null).
        var bench = await client.GetAsync($"/api/v1/portfolios/{portfolio.Id}/benchmarks?window=30d");
        Assert.Equal(HttpStatusCode.OK, bench.StatusCode);

        // 7. Get and update score configuration.
        var cfgGet = await client.GetAsync($"/api/v1/portfolios/{portfolio.Id}/score-configuration");
        Assert.Equal(HttpStatusCode.OK, cfgGet.StatusCode);
        var cfgPut = await client.PutAsJsonAsync($"/api/v1/portfolios/{portfolio.Id}/score-configuration", new
        {
            factors = new[]
            {
                new { name = "momentum", weight = 0.5d, enabled = true },
                new { name = "engagement", weight = 0.5d, enabled = true },
            },
        });
        Assert.Equal(HttpStatusCode.OK, cfgPut.StatusCode);
        var cfg = await cfgPut.Content.ReadFromJsonAsync<ScoreConfigurationDto>();
        Assert.Equal(1, cfg!.Version);

        // 8. Reject invalid weights.
        var cfgBad = await client.PutAsJsonAsync($"/api/v1/portfolios/{portfolio.Id}/score-configuration", new
        {
            factors = new[]
            {
                new { name = "momentum", weight = -0.1d, enabled = true },
            },
        });
        Assert.Equal(HttpStatusCode.BadRequest, cfgBad.StatusCode);

        // 9. Reject missing portfolio.
        var missing = await client.GetAsync($"/api/v1/portfolios/{Guid.NewGuid()}/overview");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        // 10. Remove one membership.
        var del = await client.DeleteAsync($"/api/v1/portfolios/{portfolio.Id}/repositories/{ids[0]}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
    }
}

public sealed record OverviewEnvelope(Guid PortfolioId, string Window, DateOnly WindowStart, DateOnly WindowEnd, DateTimeOffset AsOfUtc, List<OverviewRowDto> Rows);
public sealed record OverviewRowDto(Guid MembershipId, Guid RepositoryId, string NodeId, string OwnerLogin, string Name, string Role, string? Category, string Lifecycle);
