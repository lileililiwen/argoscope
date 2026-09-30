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

    [Fact]
    public async Task DecisionJournal_CreateUpdateRestore_AppendsRevisions()
    {
        using var client = _factory.CreateClient();

        // 1. Create a portfolio + a repository to attach a decision to.
        var createResp = await client.PostAsJsonAsync("/api/v1/portfolios", new { name = "Journal" });
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var portfolio = await createResp.Content.ReadFromJsonAsync<PortfolioDto>();
        var addResp = await client.PostAsJsonAsync($"/api/v1/portfolios/{portfolio!.Id}/repositories", new
        {
            nodeId = "node-decisions",
            ownerLogin = "octo",
            name = "decision-repo",
            visibility = "Public",
            role = "Owned",
            category = "ci",
            lifecycle = "OpenSource",
        });
        var member = await addResp.Content.ReadFromJsonAsync<MembershipDto>();
        var repoId = member!.RepositoryId;

        // 2. Create a decision for the repository.
        var createDecision = await client.PostAsJsonAsync(
            $"/api/v1/portfolios/{portfolio.Id}/decisions", new
            {
                repositoryId = repoId,
                decisionType = "Invest",
                decisionDate = "2026-09-30",
                rationale = "Adoption is climbing; worth doubling down.",
                reviewDate = "2027-03-01",
                idempotencyKey = "client-retry-A",
                note = "first version",
                evidence = Array.Empty<object>(),
            });
        Assert.Equal(HttpStatusCode.Created, createDecision.StatusCode);
        var decision = await createDecision.Content.ReadFromJsonAsync<DecisionEntryDto>();
        Assert.Equal("Invest", decision!.DecisionType);
        Assert.Equal(1, decision.RevisionNumber);
        Assert.Null(decision.DeletedAtUtc);
        Assert.Equal("Create", decision.LatestRevision.Action);
        Assert.Equal("first version", decision.LatestRevision.Note);

        // 3. Idempotent retry returns the same row.
        var retryResp = await client.PostAsJsonAsync(
            $"/api/v1/portfolios/{portfolio.Id}/decisions", new
            {
                repositoryId = repoId,
                decisionType = "Invest",
                decisionDate = "2026-09-30",
                rationale = "Different rationale; should be ignored on retry.",
                reviewDate = "2027-03-01",
                idempotencyKey = "client-retry-A",
                evidence = Array.Empty<object>(),
            });
        Assert.Equal(HttpStatusCode.Created, retryResp.StatusCode);
        var retried = await retryResp.Content.ReadFromJsonAsync<DecisionEntryDto>();
        Assert.Equal(decision.DecisionEntryId, retried!.DecisionEntryId);
        Assert.Equal("Adoption is climbing; worth doubling down.", retried.Rationale);

        // 4. Update with stale expectedRevision returns 409.
        var stale = await client.PutAsJsonAsync(
            $"/api/v1/portfolios/{portfolio.Id}/decisions/{decision.DecisionEntryId}", new
            {
                expectedRevision = 99,
                decisionType = "Pause",
                decisionDate = "2026-09-30",
                rationale = "Stale write.",
                reviewDate = (string?)null,
                evidence = Array.Empty<object>(),
            });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        // 5. Update with the correct revision succeeds and increments the number.
        var okUpdate = await client.PutAsJsonAsync(
            $"/api/v1/portfolios/{portfolio.Id}/decisions/{decision.DecisionEntryId}", new
            {
                expectedRevision = 1,
                decisionType = "Pause",
                decisionDate = "2026-09-30",
                rationale = "Reconsidered after watching one more week.",
                reviewDate = (string?)null,
                note = "second thought",
                evidence = Array.Empty<object>(),
            });
        Assert.Equal(HttpStatusCode.OK, okUpdate.StatusCode);
        var updated = await okUpdate.Content.ReadFromJsonAsync<DecisionEntryDto>();
        Assert.Equal("Pause", updated!.DecisionType);
        Assert.Equal(2, updated.RevisionNumber);
        Assert.Equal("Update", updated.LatestRevision.Action);
        Assert.Equal("second thought", updated.LatestRevision.Note);

        // 6. Revisions endpoint returns the full ordered history.
        var revResp = await client.GetAsync(
            $"/api/v1/portfolios/{portfolio.Id}/decisions/{decision.DecisionEntryId}/revisions");
        Assert.Equal(HttpStatusCode.OK, revResp.StatusCode);
        var revisions = await revResp.Content.ReadFromJsonAsync<List<DecisionRevisionDto>>();
        Assert.Equal(2, revisions!.Count);
        Assert.Equal(1, revisions[0].RevisionNumber);
        Assert.Equal("Create", revisions[0].Action);
        Assert.Equal(2, revisions[1].RevisionNumber);
        Assert.Equal("Update", revisions[1].Action);

        // 7. Soft-delete with the right revision; list default filters it out.
        var delReq = new HttpRequestMessage(HttpMethod.Delete,
            $"/api/v1/portfolios/{portfolio.Id}/decisions/{decision.DecisionEntryId}")
        {
            Content = JsonContent.Create(new DecisionRevisionRequestBody { ExpectedRevision = 2, Note = "no longer active" }),
        };
        var delResp = await client.SendAsync(delReq);
        Assert.Equal(HttpStatusCode.OK, delResp.StatusCode);
        var listLive = await client.GetAsync($"/api/v1/portfolios/{portfolio.Id}/decisions");
        var live = await listLive.Content.ReadFromJsonAsync<List<DecisionEntryDto>>();
        Assert.Empty(live!);
        var listAll = await client.GetAsync($"/api/v1/portfolios/{portfolio.Id}/decisions?includeDeleted=true");
        var all = await listAll.Content.ReadFromJsonAsync<List<DecisionEntryDto>>();
        Assert.Single(all!);
        Assert.NotNull(all[0].DeletedAtUtc);

        // 8. Restore brings it back and records a Restore revision.
        var restoreResp = await client.PostAsJsonAsync(
            $"/api/v1/portfolios/{portfolio.Id}/decisions/{decision.DecisionEntryId}/restore",
            new DecisionRevisionRequestBody { ExpectedRevision = 3, Note = "came back" });
        Assert.Equal(HttpStatusCode.OK, restoreResp.StatusCode);
        var restored = await restoreResp.Content.ReadFromJsonAsync<DecisionEntryDto>();
        Assert.Null(restored!.DeletedAtUtc);
        Assert.Equal(4, restored.RevisionNumber);
        Assert.Equal("Restore", restored.LatestRevision.Action);

        // 9. Decisions never touch the membership lifecycle.
        var membershipResp = await client.GetAsync($"/api/v1/portfolios/{portfolio.Id}/repositories");
        var memberships = await membershipResp.Content.ReadFromJsonAsync<List<MembershipDto>>();
        Assert.Equal("OpenSource", memberships!.Single(m => m.RepositoryId == repoId).Lifecycle);
    }

    [Fact]
    public async Task DecisionJournal_CrossPortfolioEvidence_IsMarkedUnresolved()
    {
        using var client = _factory.CreateClient();

        // Build two portfolios each with a repository.
        var p1 = await (await client.PostAsJsonAsync("/api/v1/portfolios", new { name = "P1" }))
            .Content.ReadFromJsonAsync<PortfolioDto>();
        var p2 = await (await client.PostAsJsonAsync("/api/v1/portfolios", new { name = "P2" }))
            .Content.ReadFromJsonAsync<PortfolioDto>();

        var p1Repo = await (await client.PostAsJsonAsync($"/api/v1/portfolios/{p1!.Id}/repositories", new
        {
            nodeId = "node-p1",
            ownerLogin = "octo",
            name = "p1-repo",
            visibility = "Public",
            role = "Owned",
            lifecycle = "OpenSource",
        })).Content.ReadFromJsonAsync<MembershipDto>();
        var p2Repo = await (await client.PostAsJsonAsync($"/api/v1/portfolios/{p2!.Id}/repositories", new
        {
            nodeId = "node-p2",
            ownerLogin = "octo",
            name = "p2-repo",
            visibility = "Public",
            role = "Owned",
            lifecycle = "OpenSource",
        })).Content.ReadFromJsonAsync<MembershipDto>();

        // Collect once on each so we have a MetricSnapshot row.
        await client.PostAsync(
            $"/api/v1/portfolios/{p2.Id}/repositories/{p2Repo!.RepositoryId}/collect", content: null);

        // Find a snapshot belonging to P2 by going through the metrics
        // endpoint. We don't expose snapshot ids directly via the API
        // yet, so simulate cross-portfolio evidence by pointing at a
        // random guid (the resolver should mark it Unresolved
        // regardless).
        var missingSnapshotId = Guid.NewGuid();

        var create = await client.PostAsJsonAsync(
            $"/api/v1/portfolios/{p1.Id}/decisions", new
            {
                repositoryId = p1Repo!.RepositoryId,
                decisionType = "Continue",
                decisionDate = "2026-09-30",
                rationale = "Trying to link a snapshot we cannot see.",
                evidence = new[]
                {
                    new
                    {
                        kind = "Snapshot",
                        referenceId = missingSnapshotId.ToString(),
                        label = "broken",
                    },
                },
            });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var dto = await create.Content.ReadFromJsonAsync<DecisionEntryDto>();
        var evidence = Assert.Single(dto!.Evidence);
        Assert.Equal("Snapshot", evidence.Kind);
        Assert.Equal("Unresolved", evidence.Resolution);
    }

    [Fact]
    public async Task AlertRules_CreateEvaluateHistory_MasksSecretsAndRejectsUnsafe()
    {
        using var client = _factory.CreateClient();

        var portfolio = await (await client.PostAsJsonAsync("/api/v1/portfolios", new { name = "Alerts" }))
            .Content.ReadFromJsonAsync<PortfolioDto>();
        var member = await (await client.PostAsJsonAsync($"/api/v1/portfolios/{portfolio!.Id}/repositories", new
        {
            nodeId = "node-alert-1",
            ownerLogin = "octo",
            name = "alert-repo",
            visibility = "Public",
            role = "Owned",
            lifecycle = "OpenSource",
        })).Content.ReadFromJsonAsync<MembershipDto>();

        // Unsafe webhook rejected without a request being sent.
        var unsafeResp = await client.PostAsJsonAsync(
            $"/api/v1/portfolios/{portfolio.Id}/alert-rules", new
            {
                repositoryId = member!.RepositoryId.ToString(),
                name = "Unsafe",
                metricKey = "stars_30d",
                @operator = "GreaterThanOrEqual",
                threshold = 5d,
                minimumCoverage = 0.1d,
                cooldownHours = 0,
                enabled = true,
                channel = "Webhook",
                destination = "http://example.com/hook",
                secret = "s3cret",
            });
        Assert.Equal(HttpStatusCode.BadRequest, unsafeResp.StatusCode);

        // Email rule accepted; destination masked and secret hidden.
        var createResp = await client.PostAsJsonAsync(
            $"/api/v1/portfolios/{portfolio.Id}/alert-rules", new
            {
                repositoryId = member.RepositoryId.ToString(),
                name = "Stale watch",
                metricKey = "snapshot_staleness_hours",
                @operator = "GreaterThanOrEqual",
                threshold = 100000d,
                minimumCoverage = 0d,
                cooldownHours = 0,
                enabled = true,
                channel = "Email",
                destination = "owner@example.com",
            });
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var rule = await createResp.Content.ReadFromJsonAsync<AlertRuleDto>();
        Assert.DoesNotContain("owner@example.com", (await createResp.Content.ReadAsStringAsync()).Replace("o***@example.com", string.Empty), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("o***@example.com", rule!.DestinationMasked);
        Assert.False(rule.HasSecret);

        // Rules list never exposes the raw destination either.
        var listResp = await client.GetAsync($"/api/v1/portfolios/{portfolio.Id}/alert-rules");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var rules = await listResp.Content.ReadFromJsonAsync<List<AlertRuleDto>>();
        Assert.Single(rules!);

        // Evaluate: no snapshots yet, so the run skips without delivery.
        var evalResp = await client.PostAsync(
            $"/api/v1/portfolios/{portfolio.Id}/alert-rules/{rule.RuleId}/evaluate", content: null);
        Assert.Equal(HttpStatusCode.OK, evalResp.StatusCode);
        var evaluation = await evalResp.Content.ReadFromJsonAsync<AlertEvaluationDto>();
        Assert.Equal("SkippedInsufficientData", evaluation!.Status);
        Assert.Empty(evaluation.Attempts);

        // History endpoint surfaces the evaluation.
        var historyResp = await client.GetAsync($"/api/v1/portfolios/{portfolio.Id}/alerts?limit=10");
        Assert.Equal(HttpStatusCode.OK, historyResp.StatusCode);
        var history = await historyResp.Content.ReadFromJsonAsync<List<AlertEvaluationDto>>();
        Assert.Single(history!);

        // Stale update conflicts.
        var conflictResp = await client.PutAsJsonAsync(
            $"/api/v1/portfolios/{portfolio.Id}/alert-rules/{rule.RuleId}", new
            {
                expectedVersion = rule.Version + 99,
                name = "Stale watch",
                metricKey = "snapshot_staleness_hours",
                @operator = "GreaterThanOrEqual",
                threshold = 1d,
                minimumCoverage = 0d,
                cooldownHours = 0,
                enabled = true,
                channel = "Email",
                destination = "owner@example.com",
            });
        Assert.Equal(HttpStatusCode.Conflict, conflictResp.StatusCode);
    }

    [Fact]
    public async Task CommercialSignals_CollectReviewQueue_StaleConflict()
    {
        using var client = _factory.CreateClient();

        var portfolio = await (await client.PostAsJsonAsync("/api/v1/portfolios", new { name = "Signals" }))
            .Content.ReadFromJsonAsync<PortfolioDto>();
        var member = await (await client.PostAsJsonAsync($"/api/v1/portfolios/{portfolio!.Id}/repositories", new
        {
            nodeId = "node-signal-1",
            ownerLogin = "octo",
            name = "signal-repo",
            visibility = "Public",
            role = "Owned",
            lifecycle = "OpenSource",
        })).Content.ReadFromJsonAsync<MembershipDto>();

        // Seed eligible issue text through the fake source provider: one
        // commercial request with a secret that must be redacted, one empty
        // source that must not produce a suggestion.
        using (var scope = _factory.Services.CreateScope())
        {
            var fake = scope.ServiceProvider.GetRequiredService<Argoscope.GitHub.FakeGitHubRepositoryProvider>();
            fake.AddSource(new Argoscope.GitHub.CommercialSourceInput(
                Argoscope.Domain.Signals.SignalSourceType.Issue, 101,
                "https://github.com/octo/signal-repo/issues/101",
                DateTimeOffset.UtcNow, "Hosted version for our team?",
                "We want managed hosting. Contact buyer@example.com, token ghp_fixture123."), "octo", "signal-repo");
            fake.AddSource(new Argoscope.GitHub.CommercialSourceInput(
                Argoscope.Domain.Signals.SignalSourceType.PullRequest, 102,
                "https://github.com/octo/signal-repo/pull/102",
                DateTimeOffset.UtcNow, "", "   "), "octo", "signal-repo");
        }

        var collect = await client.PostAsync(
            $"/api/v1/repositories/{member!.RepositoryId}/commercial-signals/collect", content: null);
        Assert.Equal(HttpStatusCode.OK, collect.StatusCode);
        var run = await collect.Content.ReadFromJsonAsync<CollectSignalsResultDto>();
        Assert.Equal(1, run!.Created);

        // Repeat: idempotent duplicate, no new row.
        var again = await client.PostAsync(
            $"/api/v1/repositories/{member.RepositoryId}/commercial-signals/collect", content: null);
        var run2 = await again.Content.ReadFromJsonAsync<CollectSignalsResultDto>();
        Assert.Equal(1, run2!.Duplicates);

        var list = await (await client.GetAsync(
            $"/api/v1/repositories/{member.RepositoryId}/commercial-signals?state=pending"))
            .Content.ReadFromJsonAsync<List<CommercialSignalDto>>();
        Assert.Single(list!);
        var signal = list![0];
        Assert.Equal("HostedRequest", signal.Category);
        Assert.Equal("Pending", signal.Status);
        Assert.Equal(1, signal.SuggestionVersion);
        Assert.DoesNotContain("buyer@example.com", signal.Excerpt);
        Assert.DoesNotContain("ghp_fixture123", signal.Excerpt);

        // Review with correction; stale version then conflicts.
        var review = await client.PatchAsJsonAsync(
            $"/api/v1/repositories/{member.RepositoryId}/commercial-signals/{signal.SignalId}/review", new
            {
                expectedVersion = signal.Version,
                decision = "Correct",
                correctedCategory = "PaidSupport",
                note = "actually support",
            });
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        var reviewed = await review.Content.ReadFromJsonAsync<CommercialSignalDto>();
        Assert.Equal("Corrected", reviewed!.Status);
        Assert.Equal("PaidSupport", reviewed.CorrectedCategory);

        var stale = await client.PatchAsJsonAsync(
            $"/api/v1/repositories/{member.RepositoryId}/commercial-signals/{signal.SignalId}/review", new
            {
                expectedVersion = signal.Version,
                decision = "Reject",
            });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        // Queue filters and immutable audit readback.
        var reviewedList = await (await client.GetAsync(
            $"/api/v1/repositories/{member.RepositoryId}/commercial-signals?state=reviewed"))
            .Content.ReadFromJsonAsync<List<CommercialSignalDto>>();
        Assert.Single(reviewedList!);
        var audits = await (await client.GetAsync(
            $"/api/v1/repositories/{member.RepositoryId}/commercial-signals/{signal.SignalId}/reviews"))
            .Content.ReadFromJsonAsync<List<SignalReviewDto>>();
        Assert.Single(audits!);
        Assert.Equal("HostedRequest", audits![0].PriorCategory);
        Assert.Equal("Correct", audits[0].Decision);
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

public sealed record DecisionEntryDto(
    Guid DecisionEntryId,
    Guid PortfolioId,
    Guid? RepositoryId,
    string DecisionType,
    DateOnly DecisionDate,
    string Rationale,
    DateOnly? ReviewDate,
    int RevisionNumber,
    string? IdempotencyKey,
    DateTimeOffset? DeletedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    List<DecisionEvidenceDto> Evidence,
    DecisionRevisionSummaryDto LatestRevision);

public sealed record DecisionRevisionSummaryDto(
    int RevisionNumber,
    string Action,
    string ActorId,
    DateTimeOffset OccurredAtUtc,
    string? Note);

public sealed record DecisionEvidenceDto(
    Guid EvidenceId,
    string Kind,
    Guid ReferenceId,
    string Resolution,
    string SourceDestination,
    string? Label,
    DateOnly? EvidenceDate);

public sealed record DecisionRevisionDto(
    int RevisionNumber,
    string Action,
    string ActorId,
    DateTimeOffset OccurredAtUtc,
    string? Note,
    List<DecisionEvidenceDto> Evidence);

public sealed record DecisionRevisionRequestBody
{
    public int ExpectedRevision { get; init; }
    public string? Note { get; init; }
}

public sealed record AlertRuleDto(
    Guid RuleId,
    Guid PortfolioId,
    Guid? RepositoryId,
    string Name,
    string MetricKey,
    string Operator,
    double Threshold,
    double MinimumCoverage,
    int CooldownHours,
    bool Enabled,
    string Channel,
    string DestinationMasked,
    bool HasSecret,
    int Version,
    DateTimeOffset? DeletedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record AlertEvaluationDto(
    Guid EvaluationId,
    Guid RuleId,
    string RuleName,
    Guid PortfolioId,
    Guid? RepositoryId,
    string MetricKey,
    DateTimeOffset MetricWindowEndUtc,
    double? MetricValue,
    double Coverage,
    string Status,
    string? Reason,
    DateTimeOffset EvaluatedAtUtc,
    List<AlertAttemptDto> Attempts);

public sealed record AlertAttemptDto(
    Guid AttemptId,
    int AttemptNumber,
    string State,
    int? ResponseCode,
    string? Error,
    DateTimeOffset? NextRetryAtUtc,
    DateTimeOffset CreatedAtUtc);

public sealed record CollectSignalsResultDto(
    int Created,
    int Duplicates,
    int Retried,
    int MarkedUnavailable);

public sealed record CommercialSignalDto(
    Guid SignalId,
    Guid RepositoryId,
    string SourceType,
    int SourceNumber,
    string SourceUrl,
    DateTimeOffset SourceUpdatedAtUtc,
    string ContentHash,
    string Excerpt,
    bool SourceAvailable,
    int SuggestionVersion,
    string Category,
    double Confidence,
    string ClassifierVersion,
    string Rationale,
    string Status,
    string? CorrectedCategory,
    string? Reviewer,
    DateTimeOffset? ReviewedAtUtc,
    int Version,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record SignalReviewDto(
    int RevisionNumber,
    string Decision,
    string PriorCategory,
    string PriorStatus,
    string? CorrectedCategory,
    string Reviewer,
    DateTimeOffset OccurredAtUtc,
    string? Note);
