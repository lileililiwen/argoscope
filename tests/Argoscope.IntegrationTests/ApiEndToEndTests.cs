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
using Argoscope.Packages;
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

    [Fact]
    public async Task PackageAdoption_LinkCollectAndFetch_RejectsInvalidCoordinate()
    {
        using var client = _factory.CreateClient();

        // Create portfolio + repository to attach packages to.
        var createResp = await client.PostAsJsonAsync("/api/v1/portfolios", new { name = "Packages" });
        var portfolio = await createResp.Content.ReadFromJsonAsync<PortfolioDto>();
        var addResp = await client.PostAsJsonAsync($"/api/v1/portfolios/{portfolio!.Id}/repositories", new
        {
            nodeId = "node-pkg",
            ownerLogin = "octo",
            name = "pkg-repo",
            visibility = "Public",
            role = "Owned",
            category = "ci",
            lifecycle = "OpenSource",
        });
        var member = await addResp.Content.ReadFromJsonAsync<MembershipDto>();
        var repoId = member!.RepositoryId;

        // 1. Valid coordinate accepted.
        var validResp = await client.PostAsJsonAsync($"/api/v1/repositories/{repoId}/packages", new
        {
            provider = "DockerHub",
            coordinate = "argoscope/sample-runner",
        });
        Assert.Equal(HttpStatusCode.Created, validResp.StatusCode);
        var assoc = await validResp.Content.ReadFromJsonAsync<PackageAssociationDto>();
        Assert.Equal("DockerHub", assoc!.Provider);
        Assert.Equal("argoscope/sample-runner", assoc.Coordinate);

        // 2. Invalid coordinate rejected with 400.
        var badResp = await client.PostAsJsonAsync($"/api/v1/repositories/{repoId}/packages", new
        {
            provider = "DockerHub",
            coordinate = "UpperCase/repo",
        });
        Assert.Equal(HttpStatusCode.BadRequest, badResp.StatusCode);

        // 3. List associations shows the one we created.
        var listResp = await client.GetAsync($"/api/v1/repositories/{repoId}/packages");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var list = await listResp.Content.ReadFromJsonAsync<List<PackageAssociationDto>>();
        Assert.Single(list!);

        // 4. On-demand collection runs (the fake provider in the test
        // factory returns no observations, so the run succeeds and
        // reports zero writes; the association remains linked).
        var collectResp = await client.PostAsync(
            $"/api/v1/repositories/{repoId}/packages/{assoc.AssociationId}/collect",
            content: null);
        Assert.Equal(HttpStatusCode.OK, collectResp.StatusCode);
        var runResult = await collectResp.Content.ReadFromJsonAsync<PackageCollectionRunResultDto>();
        Assert.Equal(0, runResult!.ObservationsWritten);

        // 5. Adoption report returns the linked series with no data.
        var adoptionResp = await client.GetAsync($"/api/v1/repositories/{repoId}/adoption");
        Assert.Equal(HttpStatusCode.OK, adoptionResp.StatusCode);
        var adoption = await adoptionResp.Content.ReadFromJsonAsync<PackageAdoptionReportDto>();
        Assert.Equal(repoId, adoption!.RepositoryId);
        Assert.Single(adoption.Series);

        // 6. Remove association.
        var delResp = await client.DeleteAsync($"/api/v1/repositories/{repoId}/packages/{assoc.AssociationId}");
        Assert.Equal(HttpStatusCode.NoContent, delResp.StatusCode);
    }
}

public sealed record OverviewEnvelope(Guid PortfolioId, string Window, DateOnly WindowStart, DateOnly WindowEnd, DateTimeOffset AsOfUtc, List<OverviewRowDto> Rows);
public sealed record OverviewRowDto(Guid MembershipId, Guid RepositoryId, string NodeId, string OwnerLogin, string Name, string Role, string? Category, string Lifecycle);

public sealed record PackageAssociationDto(
    Guid AssociationId,
    Guid RepositoryId,
    string Provider,
    string Coordinate,
    string DefaultUnit,
    string DefaultWindow,
    string Status,
    string? AttentionReason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record PackageCollectionRunResultDto(
    Guid AssociationId,
    int ObservationsWritten,
    int ObservationsPreserved,
    string MetadataStatus,
    string PageStatus,
    string? DiagnosticCode,
    DateTimeOffset RunAtUtc);

public sealed record PackageAdoptionReportDto(
    Guid RepositoryId,
    DateTimeOffset AsOfUtc,
    List<PackageAdoptionSeriesDto> Series,
    int SeriesWithData,
    int SeriesStale,
    int SeriesMissing,
    string? InsufficientReason);

public sealed record PackageAdoptionSeriesDto(
    Guid AssociationId,
    string Provider,
    string Coordinate,
    string Unit,
    string Window,
    DateTimeOffset FirstObservedAtUtc,
    DateTimeOffset LastObservedAtUtc,
    int ExpectedPoints,
    int ActualPoints,
    double Coverage,
    string Status,
    List<PackageAdoptionPointDto> Points);

public sealed record PackageAdoptionPointDto(
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    DateTimeOffset ObservedAtUtc,
    double Value,
    string Status,
    bool IsComplete,
    string? DiagnosticCode);
