using System.Text.Json;
using System.Text.Json.Serialization;
using Argoscope.Application.Alerts;
using Argoscope.Application.Analytics;
using Argoscope.Application.Collection;
using Argoscope.Application.Decisions;
using Argoscope.Application.Packages;
using Argoscope.Application.Portfolios;
using Argoscope.Application.Ranking;
using Argoscope.Application.Signals;
using Argoscope.Domain.Common;
using Argoscope.Domain.Decisions;
using Argoscope.Domain.Memberships;
using Argoscope.Domain.Packages;
using Argoscope.Domain.Portfolios;
using Argoscope.Domain.Repositories;
using Argoscope.Domain.Scores;
using Argoscope.Domain.Snapshots;
using Argoscope.GitHub;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Argoscope.Api;

/// <summary>JSON serialization options for the API surface.</summary>
public static class ArgoscopeJson
{
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(), new IdJsonConverter() },
    };

    public static void Apply(JsonSerializerOptions target)
    {
        target.PropertyNamingPolicy = Web.PropertyNamingPolicy;
        target.DefaultIgnoreCondition = Web.DefaultIgnoreCondition;
        target.WriteIndented = Web.WriteIndented;
        foreach (var converter in Web.Converters)
        {
            target.Converters.Add(converter);
        }
    }
}

/// <summary>Maps the v1 API endpoints. Returns RFC 7807 problem details for failures.</summary>
public static class ArgoscopeApiModule
{
    public static IServiceCollection AddArgoscopeApi(this IServiceCollection services) => services;

    public static IEndpointRouteBuilder MapArgoscopeApi(this IEndpointRouteBuilder builder)
    {
        var v1 = builder.MapGroup("/api/v1").WithGroupName("argoscope");
        v1.AddEndpointFilter<TenantAuthorizationFilter>();

        v1.MapPost("/portfolios", (CreatePortfolioRequest request, HttpContext http, PortfolioService svc, CancellationToken ct) => CreatePortfolioAsync(request, http, svc, ct));
        v1.MapGet("/portfolios", (HttpContext http, IPortfolioRepository repo, CancellationToken ct) => ListPortfoliosAsync(http, repo, ct));
        v1.MapGet("/portfolios/{portfolioId:guid}", (Guid portfolioId, IPortfolioRepository repo, CancellationToken ct) => GetPortfolioAsync(portfolioId, repo, ct));
        v1.MapPost("/portfolios/{portfolioId:guid}/repositories", (Guid portfolioId, AddRepositoryRequest request, PortfolioService svc, CancellationToken ct) => AddRepositoryAsync(portfolioId, request, svc, ct));
        v1.MapGet("/portfolios/{portfolioId:guid}/repositories", (Guid portfolioId, PortfolioService svc, CancellationToken ct) => ListRepositoriesAsync(portfolioId, svc, ct));
        v1.MapPut("/portfolios/{portfolioId:guid}/repositories/{repositoryId:guid}", (Guid portfolioId, Guid repositoryId, UpdateMembershipRequest request, PortfolioService svc, CancellationToken ct) => UpdateMembershipAsync(portfolioId, repositoryId, request, svc, ct));
        v1.MapDelete("/portfolios/{portfolioId:guid}/repositories/{repositoryId:guid}", (Guid portfolioId, Guid repositoryId, PortfolioService svc, CancellationToken ct) => RemoveMembershipAsync(portfolioId, repositoryId, svc, ct));
        v1.MapGet("/portfolios/{portfolioId:guid}/overview", (Guid portfolioId, string? window, AnalyticsService svc, CancellationToken ct) => GetOverviewAsync(portfolioId, window, svc, ct));
        v1.MapGet("/portfolios/{portfolioId:guid}/benchmarks", (Guid portfolioId, string? window, AnalyticsService svc, CancellationToken ct) => GetBenchmarksAsync(portfolioId, window, svc, ct));
        v1.MapGet("/portfolios/{portfolioId:guid}/score-configuration", (Guid portfolioId, PriorityScoreService svc, CancellationToken ct) => GetScoreConfigurationAsync(portfolioId, svc, ct));
        v1.MapPut("/portfolios/{portfolioId:guid}/score-configuration", (Guid portfolioId, ScoreConfigurationRequest request, PriorityScoreService svc, CancellationToken ct) => PutScoreConfigurationAsync(portfolioId, request, svc, ct));
        v1.MapGet("/repositories/{repositoryId:guid}/metrics", (Guid repositoryId, AnalyticsService svc, CancellationToken ct) => GetRepositoryMetricsAsync(repositoryId, svc, ct));
        v1.MapPost("/portfolios/{portfolioId:guid}/repositories/{repositoryId:guid}/collect", (Guid portfolioId, Guid repositoryId, CollectionService svc, IRepositoryStore store, CancellationToken ct) => CollectNowAsync(portfolioId, repositoryId, svc, store, ct));

        // Package adoption: owner-managed associations, on-demand
        // collection, and the read-side adoption report.
        v1.MapGet("/repositories/{repositoryId:guid}/packages", (Guid repositoryId, PackageAssociationService svc, CancellationToken ct) => ListPackageAssociationsAsync(repositoryId, svc, ct));
        v1.MapPost("/repositories/{repositoryId:guid}/packages", (Guid repositoryId, CreatePackageAssociationRequest request, PackageAssociationService svc, CancellationToken ct) => CreatePackageAssociationAsync(repositoryId, request, svc, ct));
        v1.MapPut("/repositories/{repositoryId:guid}/packages/{associationId:guid}", (Guid repositoryId, Guid associationId, UpdatePackageAssociationRequest request, PackageAssociationService svc, CancellationToken ct) => UpdatePackageAssociationAsync(repositoryId, associationId, request, svc, ct));
        v1.MapDelete("/repositories/{repositoryId:guid}/packages/{associationId:guid}", (Guid repositoryId, Guid associationId, PackageAssociationService svc, CancellationToken ct) => RemovePackageAssociationAsync(repositoryId, associationId, svc, ct));
        v1.MapPost("/repositories/{repositoryId:guid}/packages/{associationId:guid}/collect", (Guid repositoryId, Guid associationId, PackageCollectionService svc, PackageAssociationService assocSvc, CancellationToken ct) => CollectPackageNowAsync(repositoryId, associationId, svc, assocSvc, ct));
        v1.MapGet("/repositories/{repositoryId:guid}/adoption", (Guid repositoryId, PackageAdoptionService svc, CancellationToken ct) => GetAdoptionAsync(repositoryId, svc, ct));

        // Decision journal: owner-authored decisions with append-only
        // revision history and typed, same-portfolio evidence
        // references. The read API surfaces unresolved references as a
        // broken-link marker rather than dropping them.
        v1.MapPost("/portfolios/{portfolioId:guid}/decisions", (Guid portfolioId, CreateDecisionRequest request, DecisionService svc, CancellationToken ct) => CreateDecisionAsync(portfolioId, request, svc, ct));
        v1.MapGet("/portfolios/{portfolioId:guid}/decisions", (Guid portfolioId, string? includeDeleted, DecisionService svc, CancellationToken ct) => ListDecisionsAsync(portfolioId, includeDeleted, svc, ct));
        v1.MapGet("/portfolios/{portfolioId:guid}/decisions/{decisionId:guid}", (Guid portfolioId, Guid decisionId, DecisionService svc, CancellationToken ct) => GetDecisionAsync(portfolioId, decisionId, svc, ct));
        v1.MapPut("/portfolios/{portfolioId:guid}/decisions/{decisionId:guid}", (Guid portfolioId, Guid decisionId, UpdateDecisionRequest request, DecisionService svc, CancellationToken ct) => UpdateDecisionAsync(portfolioId, decisionId, request, svc, ct));
        v1.MapDelete("/portfolios/{portfolioId:guid}/decisions/{decisionId:guid}", (Guid portfolioId, Guid decisionId, [FromBody] DecisionRevisionRequest request, DecisionService svc, CancellationToken ct) => DeleteDecisionAsync(portfolioId, decisionId, request, svc, ct));
        v1.MapPost("/portfolios/{portfolioId:guid}/decisions/{decisionId:guid}/restore", (Guid portfolioId, Guid decisionId, [FromBody] DecisionRevisionRequest request, DecisionService svc, CancellationToken ct) => RestoreDecisionAsync(portfolioId, decisionId, request, svc, ct));
        v1.MapGet("/portfolios/{portfolioId:guid}/decisions/{decisionId:guid}/revisions", (Guid portfolioId, Guid decisionId, DecisionService svc, CancellationToken ct) => GetDecisionRevisionsAsync(portfolioId, decisionId, svc, ct));

        // Portfolio attention alerts: validated rules over the
        // allowlisted metric set, deduplicated evaluation and
        // observable delivery history. Secrets are write-only.
        v1.MapPost("/portfolios/{portfolioId:guid}/alert-rules", (Guid portfolioId, CreateAlertRuleRequest request, AlertService svc, CancellationToken ct) => CreateAlertRuleAsync(portfolioId, request, svc, ct));
        v1.MapGet("/portfolios/{portfolioId:guid}/alert-rules", (Guid portfolioId, AlertService svc, CancellationToken ct) => ListAlertRulesAsync(portfolioId, svc, ct));
        v1.MapPut("/portfolios/{portfolioId:guid}/alert-rules/{ruleId:guid}", (Guid portfolioId, Guid ruleId, UpdateAlertRuleRequest request, AlertService svc, CancellationToken ct) => UpdateAlertRuleAsync(portfolioId, ruleId, request, svc, ct));
        v1.MapDelete("/portfolios/{portfolioId:guid}/alert-rules/{ruleId:guid}", (Guid portfolioId, Guid ruleId, [FromBody] AlertRuleRevisionRequest request, AlertService svc, CancellationToken ct) => DeleteAlertRuleAsync(portfolioId, ruleId, request, svc, ct));
        v1.MapPost("/portfolios/{portfolioId:guid}/alert-rules/{ruleId:guid}/evaluate", (Guid portfolioId, Guid ruleId, AlertService svc, CancellationToken ct) => EvaluateAlertRuleAsync(portfolioId, ruleId, svc, ct));
        v1.MapGet("/portfolios/{portfolioId:guid}/alerts", (Guid portfolioId, int? limit, AlertService svc, CancellationToken ct) => ListAlertsAsync(portfolioId, limit, svc, ct));

        // Commercial signals: bounded issue/PR projections, advisory
        // classification and owner review. Read-only toward GitHub; never
        // alters scores, lifecycle state or labels.
        v1.MapPost("/repositories/{repositoryId:guid}/commercial-signals/collect", (Guid repositoryId, CommercialSignalService svc, CancellationToken ct) => CollectSignalsAsync(repositoryId, svc, ct));
        v1.MapGet("/repositories/{repositoryId:guid}/commercial-signals", (Guid repositoryId, string? state, CommercialSignalService svc, CancellationToken ct) => ListSignalsAsync(repositoryId, state, svc, ct));
        v1.MapGet("/repositories/{repositoryId:guid}/commercial-signals/{signalId:guid}", (Guid repositoryId, Guid signalId, CommercialSignalService svc, CancellationToken ct) => GetSignalAsync(repositoryId, signalId, svc, ct));
        v1.MapGet("/repositories/{repositoryId:guid}/commercial-signals/{signalId:guid}/reviews", (Guid repositoryId, Guid signalId, CommercialSignalService svc, CancellationToken ct) => GetSignalReviewsAsync(repositoryId, signalId, svc, ct));
        v1.MapPatch("/repositories/{repositoryId:guid}/commercial-signals/{signalId:guid}/review", (Guid repositoryId, Guid signalId, ReviewSignalRequest request, CommercialSignalService svc, CancellationToken ct) => ReviewSignalAsync(repositoryId, signalId, request, svc, ct));

        return builder;
    }

    private static async Task<IResult> CreatePortfolioAsync(CreatePortfolioRequest request, HttpContext http, PortfolioService svc, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.Name))
        {
            return Problem(StatusCodes.Status400BadRequest, "validation", "Name is required.");
        }
        var principal = TenantHttp.GetPrincipal(http);
        var result = await svc.CreateAsync(new CreatePortfolioCommand(request.Name, DateTimeOffset.UtcNow, principal?.TenantId), ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Problem(StatusCodes.Status400BadRequest, result.Error!.Value.Code, result.Error.Value.Message);
        }
        return Results.Created($"/api/v1/portfolios/{result.Value.Id}", result.Value);
    }

    private static async Task<IResult> ListPortfoliosAsync(HttpContext http, IPortfolioRepository portfolioRepository, CancellationToken ct)
    {
        var principal = TenantHttp.GetPrincipal(http);
        var ids = await portfolioRepository.ListAllAsync(ct).ConfigureAwait(false);
        var list = new List<PortfolioDto>();
        foreach (var id in ids)
        {
            var dto = await GetPortfolioInternalAsync(id, portfolioRepository, ct).ConfigureAwait(false);
            // Hosted mode lists only the caller's tenant; SingleOwner mode
            // (no principal) preserves the legacy full listing.
            if (dto is null) continue;
            if (principal is not null && dto.TenantId != principal.TenantId.Value) continue;
            list.Add(dto);
        }
        return Results.Ok(list);
    }

    private static async Task<PortfolioDto?> GetPortfolioInternalAsync(Id<Portfolio> id, IPortfolioRepository repo, CancellationToken ct)
    {
        var p = await repo.FindAsync(id, ct).ConfigureAwait(false);
        return p is null ? null : new PortfolioDto(p.Id.Value, p.Name, p.CreatedAtUtc, p.UpdatedAtUtc, p.TenantId?.Value);
    }

    private static async Task<IResult> GetPortfolioAsync(Guid portfolioId, IPortfolioRepository portfolioRepository, CancellationToken ct)
    {
        var dto = await GetPortfolioInternalAsync(Id<Portfolio>.From(portfolioId), portfolioRepository, ct).ConfigureAwait(false);
        return dto is null ? Results.NotFound() : Results.Ok(dto);
    }

    private static async Task<IResult> AddRepositoryAsync(Guid portfolioId, AddRepositoryRequest request, PortfolioService svc, CancellationToken ct)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.NodeId)
            || string.IsNullOrWhiteSpace(request.OwnerLogin)
            || string.IsNullOrWhiteSpace(request.Name)
            || string.IsNullOrWhiteSpace(request.Lifecycle))
        {
            return Problem(StatusCodes.Status400BadRequest, "validation", "NodeId, OwnerLogin, Name, Role and Lifecycle are required.");
        }
        if (!Enum.TryParse<MembershipRole>(request.Role, ignoreCase: true, out var role))
        {
            return Problem(StatusCodes.Status400BadRequest, "validation", "Role must be 'Owned' or 'Competitor'.");
        }
        if (!Enum.TryParse<RepositoryVisibility>(request.Visibility, ignoreCase: true, out var visibility))
        {
            return Problem(StatusCodes.Status400BadRequest, "validation", "Visibility must be Public, Private, Internal or Unknown.");
        }
        var result = await svc.AddRepositoryAsync(new AddRepositoryCommand(
            Id<Portfolio>.From(portfolioId),
            request.NodeId, request.OwnerLogin, request.Name, visibility,
            role, request.Category, request.Lifecycle, DateTimeOffset.UtcNow), ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Problem(result.Error!.Value.Code == "not_found" ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest,
                result.Error.Value.Code, result.Error.Value.Message);
        }
        return Results.Created($"/api/v1/portfolios/{portfolioId}/repositories/{result.Value.RepositoryId}", result.Value);
    }

    private static async Task<IResult> ListRepositoriesAsync(Guid portfolioId, PortfolioService svc, CancellationToken ct)
    {
        var list = await svc.ListMembershipsAsync(Id<Portfolio>.From(portfolioId), ct).ConfigureAwait(false);
        return Results.Ok(list);
    }

    private static async Task<IResult> UpdateMembershipAsync(Guid portfolioId, Guid repositoryId, UpdateMembershipRequest request, PortfolioService svc, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Lifecycle))
        {
            return Problem(StatusCodes.Status400BadRequest, "validation", "Lifecycle is required.");
        }
        var result = await svc.UpdateMembershipAsync(new UpdateMembershipCommand(
            Id<Portfolio>.From(portfolioId), Id<Repository>.From(repositoryId), request.Category, request.Lifecycle, DateTimeOffset.UtcNow), ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Problem(result.Error!.Value.Code == "not_found" ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest,
                result.Error.Value.Code, result.Error.Value.Message);
        }
        return Results.Ok(result.Value);
    }

    private static async Task<IResult> RemoveMembershipAsync(Guid portfolioId, Guid repositoryId, PortfolioService svc, CancellationToken ct)
    {
        var result = await svc.RemoveMembershipAsync(Id<Portfolio>.From(portfolioId), Id<Repository>.From(repositoryId), ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Problem(result.Error!.Value.Code == "not_found" ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest,
                result.Error.Value.Code, result.Error.Value.Message);
        }
        return Results.NoContent();
    }

    private static async Task<IResult> GetOverviewAsync(Guid portfolioId, string? window, AnalyticsService svc, CancellationToken ct)
    {
        var w = string.IsNullOrWhiteSpace(window) ? "30d" : window;
        var result = await svc.BuildOverviewAsync(Id<Portfolio>.From(portfolioId), w, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> GetBenchmarksAsync(Guid portfolioId, string? window, AnalyticsService svc, CancellationToken ct)
    {
        var w = string.IsNullOrWhiteSpace(window) ? "30d" : window;
        var result = await svc.BuildBenchmarksAsync(Id<Portfolio>.From(portfolioId), w, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> GetScoreConfigurationAsync(Guid portfolioId, PriorityScoreService svc, CancellationToken ct)
    {
        var config = await svc.GetConfigurationAsync(Id<Portfolio>.From(portfolioId), ct).ConfigureAwait(false);
        if (config is null)
        {
            var defaults = FactorNames.BriefDefaults
                .Select(kv => new ScoreFactor(kv.Key, kv.Value, enabled: true))
                .ToList();
            return Results.Ok(new ScoreConfigurationDto(0, defaults, true));
        }
        return Results.Ok(new ScoreConfigurationDto(config.Version, config.Factors, false));
    }

    private static async Task<IResult> PutScoreConfigurationAsync(Guid portfolioId, ScoreConfigurationRequest request, PriorityScoreService svc, CancellationToken ct)
    {
        if (request?.Factors is null || request.Factors.Count == 0)
        {
            return Problem(StatusCodes.Status400BadRequest, "validation", "At least one factor is required.");
        }
        ScoreFactor[] factors;
        try
        {
            factors = request.Factors
                .Select(f => new ScoreFactor(f.Name, f.Weight, f.Enabled))
                .ToArray();
        }
        catch (DomainException ex)
        {
            return Problem(StatusCodes.Status400BadRequest, "validation", ex.Message);
        }
        var result = await svc.UpdateConfigurationAsync(
            new UpdateScoreConfigurationCommand(Id<Portfolio>.From(portfolioId), factors, DateTimeOffset.UtcNow), ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Problem(result.Error!.Value.Code == "not_found" ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest,
                result.Error.Value.Code, result.Error.Value.Message);
        }
        return Results.Ok(new ScoreConfigurationDto(result.Value!.Version, result.Value.Factors, false));
    }

    private static async Task<IResult> GetRepositoryMetricsAsync(Guid repositoryId, AnalyticsService svc, CancellationToken ct)
    {
        var result = await svc.BuildRepositoryMetricsAsync(Id<Repository>.From(repositoryId), DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> CollectNowAsync(Guid portfolioId, Guid repositoryId, CollectionService svc, IRepositoryStore store, CancellationToken ct)
    {
        var repo = await store.FindAsync(Id<Repository>.From(repositoryId), ct).ConfigureAwait(false);
        if (repo is null)
        {
            return Results.NotFound();
        }
        var result = await svc.RunAsync(
            new CollectionRequest(Id<Portfolio>.From(portfolioId), repo.OwnerLogin, repo.Name, DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime), DateTimeOffset.UtcNow),
            ct).ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static async Task<IResult> ListPackageAssociationsAsync(Guid repositoryId, PackageAssociationService svc, CancellationToken ct)
    {
        var list = await svc.ListByRepositoryAsync(Id<Repository>.From(repositoryId), ct).ConfigureAwait(false);
        return Results.Ok(list);
    }

    private static async Task<IResult> CreatePackageAssociationAsync(
        Guid repositoryId,
        CreatePackageAssociationRequest request,
        PackageAssociationService svc,
        CancellationToken ct)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.Provider)
            || string.IsNullOrWhiteSpace(request.Coordinate))
        {
            return Problem(StatusCodes.Status400BadRequest, "validation", "Provider and coordinate are required.");
        }
        if (!Enum.TryParse<PackageProvider>(request.Provider, ignoreCase: true, out var provider))
        {
            return Problem(StatusCodes.Status400BadRequest, "validation",
                $"Provider must be one of: {string.Join(", ", Enum.GetNames<PackageProvider>())}.");
        }
        PackageUnit? unit = null;
        PackageWindow? window = null;
        if (!string.IsNullOrWhiteSpace(request.DefaultUnit))
        {
            if (!Enum.TryParse<PackageUnit>(request.DefaultUnit, ignoreCase: true, out var parsedUnit))
            {
                return Problem(StatusCodes.Status400BadRequest, "validation",
                    $"DefaultUnit must be one of: {string.Join(", ", Enum.GetNames<PackageUnit>())}.");
            }
            unit = parsedUnit;
        }
        if (!string.IsNullOrWhiteSpace(request.DefaultWindow))
        {
            if (!Enum.TryParse<PackageWindow>(request.DefaultWindow, ignoreCase: true, out var parsedWindow))
            {
                return Problem(StatusCodes.Status400BadRequest, "validation",
                    $"DefaultWindow must be one of: {string.Join(", ", Enum.GetNames<PackageWindow>())}.");
            }
            window = parsedWindow;
        }
        var result = await svc.CreateAsync(
            new CreatePackageAssociationCommand(
                Id<Repository>.From(repositoryId),
                provider,
                request.Coordinate,
                unit,
                window,
                DateTimeOffset.UtcNow),
            ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            var code = result.Error!.Value.Code;
            return Problem(
                code == "not_found" ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest,
                code,
                result.Error.Value.Message);
        }
        return Results.Created($"/api/v1/repositories/{repositoryId}/packages/{result.Value.AssociationId}", result.Value);
    }

    private static async Task<IResult> UpdatePackageAssociationAsync(
        Guid repositoryId,
        Guid associationId,
        UpdatePackageAssociationRequest request,
        PackageAssociationService svc,
        CancellationToken ct)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.DefaultUnit)
            || string.IsNullOrWhiteSpace(request.DefaultWindow))
        {
            return Problem(StatusCodes.Status400BadRequest, "validation", "DefaultUnit and DefaultWindow are required.");
        }
        if (!Enum.TryParse<PackageUnit>(request.DefaultUnit, ignoreCase: true, out var unit))
        {
            return Problem(StatusCodes.Status400BadRequest, "validation",
                $"DefaultUnit must be one of: {string.Join(", ", Enum.GetNames<PackageUnit>())}.");
        }
        if (!Enum.TryParse<PackageWindow>(request.DefaultWindow, ignoreCase: true, out var window))
        {
            return Problem(StatusCodes.Status400BadRequest, "validation",
                $"DefaultWindow must be one of: {string.Join(", ", Enum.GetNames<PackageWindow>())}.");
        }
        var result = await svc.UpdateAsync(
            new UpdatePackageAssociationCommand(Id<PackageAssociation>.From(associationId), unit, window, DateTimeOffset.UtcNow),
            ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            var code = result.Error!.Value.Code;
            var status = code switch
            {
                "not_found" => StatusCodes.Status404NotFound,
                "conflict" => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest,
            };
            return Problem(status, code, result.Error.Value.Message);
        }
        return Results.Ok(result.Value);
    }

    private static async Task<IResult> RemovePackageAssociationAsync(Guid repositoryId, Guid associationId, PackageAssociationService svc, CancellationToken ct)
    {
        var result = await svc.RemoveAsync(
            new RemovePackageAssociationCommand(Id<PackageAssociation>.From(associationId), DateTimeOffset.UtcNow), ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            var code = result.Error!.Value.Code;
            return Problem(code == "not_found" ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest, code, result.Error.Value.Message);
        }
        return Results.NoContent();
    }

    private static async Task<IResult> CollectPackageNowAsync(
        Guid repositoryId,
        Guid associationId,
        PackageCollectionService svc,
        PackageAssociationService assocSvc,
        CancellationToken ct)
    {
        var list = await assocSvc.ListByRepositoryAsync(Id<Repository>.From(repositoryId), ct).ConfigureAwait(false);
        var assoc = list.FirstOrDefault(a => a.AssociationId == associationId);
        if (assoc is null)
        {
            return Results.NotFound();
        }
        if (!Enum.TryParse<PackageProvider>(assoc.Provider, out var provider)
            || !Enum.TryParse<PackageUnit>(assoc.DefaultUnit, out var unit)
            || !Enum.TryParse<PackageWindow>(assoc.DefaultWindow, out var window))
        {
            return Problem(StatusCodes.Status500InternalServerError, "validation", "Association has invalid provider/unit/window.");
        }
        var result = await svc.RunAsync(
            new PackageCollectionRequest(
                Id<Repository>.From(repositoryId),
                Id<PackageAssociation>.From(associationId),
                provider,
                assoc.Coordinate,
                unit,
                window,
                DateTimeOffset.UtcNow),
            ct).ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetAdoptionAsync(Guid repositoryId, PackageAdoptionService svc, CancellationToken ct)
    {
        var report = await svc.BuildForRepositoryAsync(Id<Repository>.From(repositoryId), ct).ConfigureAwait(false);
        return report is null ? Results.NotFound() : Results.Ok(report);
    }

    private static IResult Problem(int statusCode, string code, string detail) =>
        Results.Problem(statusCode: statusCode, title: code, detail: detail);

    private static async Task<IResult> CreateDecisionAsync(
        Guid portfolioId,
        CreateDecisionRequest request,
        DecisionService svc,
        CancellationToken ct)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.DecisionType)
            || string.IsNullOrWhiteSpace(request.Rationale))
        {
            return Problem(StatusCodes.Status400BadRequest, "validation", "DecisionType and Rationale are required.");
        }
        if (!DateOnly.TryParse(request.DecisionDate, out var decisionDate))
        {
            return Problem(StatusCodes.Status400BadRequest, "validation", "DecisionDate must be an ISO-8601 date (YYYY-MM-DD).");
        }
        DateOnly? reviewDate = null;
        if (!string.IsNullOrWhiteSpace(request.ReviewDate))
        {
            if (!DateOnly.TryParse(request.ReviewDate, out var parsed))
            {
                return Problem(StatusCodes.Status400BadRequest, "validation", "ReviewDate must be an ISO-8601 date (YYYY-MM-DD).");
            }
            reviewDate = parsed;
        }
        Guid? repositoryId = null;
        if (!string.IsNullOrWhiteSpace(request.RepositoryId) && Guid.TryParse(request.RepositoryId, out var parsedRepo))
        {
            repositoryId = parsedRepo;
        }
        var evidence = (request.Evidence ?? Array.Empty<CreateDecisionEvidenceRequest>())
            .Select(e => new CreateDecisionEvidenceCommand(
                e.Kind ?? "",
                Guid.TryParse(e.ReferenceId, out var refId) ? refId : Guid.Empty,
                e.Label))
            .ToList();
        var result = await svc.CreateAsync(
            new CreateDecisionCommand(
                Id<Portfolio>.From(portfolioId),
                repositoryId.HasValue ? Id<Repository>.From(repositoryId.Value) : (Id<Repository>?)null,
                request.DecisionType,
                decisionDate,
                request.Rationale,
                reviewDate,
                request.IdempotencyKey,
                request.Note,
                evidence,
                request.ActorId ?? DecisionActor.DefaultOwner,
                DateTimeOffset.UtcNow),
            ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Problem(MapStatus(result.Error!.Value.Code), result.Error.Value.Code, result.Error.Value.Message);
        }
        return Results.Created($"/api/v1/portfolios/{portfolioId}/decisions/{result.Value.DecisionEntryId}", result.Value);
    }

    private static async Task<IResult> ListDecisionsAsync(
        Guid portfolioId,
        string? includeDeleted,
        DecisionService svc,
        CancellationToken ct)
    {
        var include = string.Equals(includeDeleted, "true", StringComparison.OrdinalIgnoreCase);
        var list = await svc.ListByPortfolioAsync(Id<Portfolio>.From(portfolioId), include, ct).ConfigureAwait(false);
        return Results.Ok(list);
    }

    private static async Task<IResult> GetDecisionAsync(
        Guid portfolioId,
        Guid decisionId,
        DecisionService svc,
        CancellationToken ct)
    {
        var dto = await svc.GetAsync(
            Id<Portfolio>.From(portfolioId),
            Id<DecisionEntry>.From(decisionId),
            ct).ConfigureAwait(false);
        return dto is null ? Results.NotFound() : Results.Ok(dto);
    }

    private static async Task<IResult> UpdateDecisionAsync(
        Guid portfolioId,
        Guid decisionId,
        UpdateDecisionRequest request,
        DecisionService svc,
        CancellationToken ct)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.DecisionType)
            || string.IsNullOrWhiteSpace(request.Rationale))
        {
            return Problem(StatusCodes.Status400BadRequest, "validation", "DecisionType and Rationale are required.");
        }
        if (!DateOnly.TryParse(request.DecisionDate, out var decisionDate))
        {
            return Problem(StatusCodes.Status400BadRequest, "validation", "DecisionDate must be an ISO-8601 date (YYYY-MM-DD).");
        }
        DateOnly? reviewDate = null;
        if (!string.IsNullOrWhiteSpace(request.ReviewDate))
        {
            if (!DateOnly.TryParse(request.ReviewDate, out var parsed))
            {
                return Problem(StatusCodes.Status400BadRequest, "validation", "ReviewDate must be an ISO-8601 date (YYYY-MM-DD).");
            }
            reviewDate = parsed;
        }
        var evidence = (request.Evidence ?? Array.Empty<CreateDecisionEvidenceRequest>())
            .Select(e => new CreateDecisionEvidenceCommand(
                e.Kind ?? "",
                Guid.TryParse(e.ReferenceId, out var refId) ? refId : Guid.Empty,
                e.Label))
            .ToList();
        var result = await svc.UpdateAsync(
            new UpdateDecisionCommand(
                Id<Portfolio>.From(portfolioId),
                Id<DecisionEntry>.From(decisionId),
                request.ExpectedRevision,
                request.DecisionType,
                decisionDate,
                request.Rationale,
                reviewDate,
                request.Note,
                evidence,
                request.ActorId ?? DecisionActor.DefaultOwner,
                DateTimeOffset.UtcNow),
            ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Problem(MapStatus(result.Error!.Value.Code), result.Error.Value.Code, result.Error.Value.Message);
        }
        return Results.Ok(result.Value);
    }

    private static async Task<IResult> DeleteDecisionAsync(
        Guid portfolioId,
        Guid decisionId,
        [FromBody] DecisionRevisionRequest request,
        DecisionService svc,
        CancellationToken ct)
    {
        if (request is null)
        {
            return Problem(StatusCodes.Status400BadRequest, "validation", "expectedRevision is required.");
        }
        var result = await svc.DeleteAsync(
            new DeleteDecisionCommand(
                Id<Portfolio>.From(portfolioId),
                Id<DecisionEntry>.From(decisionId),
                request.ExpectedRevision,
                request.Note,
                request.ActorId ?? DecisionActor.DefaultOwner,
                DateTimeOffset.UtcNow),
            ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Problem(MapStatus(result.Error!.Value.Code), result.Error.Value.Code, result.Error.Value.Message);
        }
        return Results.Ok(result.Value);
    }

    private static async Task<IResult> RestoreDecisionAsync(
        Guid portfolioId,
        Guid decisionId,
        [FromBody] DecisionRevisionRequest request,
        DecisionService svc,
        CancellationToken ct)
    {
        if (request is null)
        {
            return Problem(StatusCodes.Status400BadRequest, "validation", "expectedRevision is required.");
        }
        var result = await svc.RestoreAsync(
            new RestoreDecisionCommand(
                Id<Portfolio>.From(portfolioId),
                Id<DecisionEntry>.From(decisionId),
                request.ExpectedRevision,
                request.Note,
                request.ActorId ?? DecisionActor.DefaultOwner,
                DateTimeOffset.UtcNow),
            ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Problem(MapStatus(result.Error!.Value.Code), result.Error.Value.Code, result.Error.Value.Message);
        }
        return Results.Ok(result.Value);
    }

    private static async Task<IResult> GetDecisionRevisionsAsync(
        Guid portfolioId,
        Guid decisionId,
        DecisionService svc,
        CancellationToken ct)
    {
        var dto = await svc.GetAsync(
            Id<Portfolio>.From(portfolioId),
            Id<DecisionEntry>.From(decisionId),
            ct).ConfigureAwait(false);
        if (dto is null) return Results.NotFound();
        var revisions = await svc.GetRevisionsAsync(Id<DecisionEntry>.From(decisionId), ct).ConfigureAwait(false);
        return Results.Ok(revisions);
    }

    private static int MapStatus(string code) => code switch
    {
        "not_found" => StatusCodes.Status404NotFound,
        "conflict" => StatusCodes.Status409Conflict,
        "unauthorized" => StatusCodes.Status401Unauthorized,
        "forbidden" => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status400BadRequest,
    };

    private static async Task<IResult> CreateAlertRuleAsync(
        Guid portfolioId,
        CreateAlertRuleRequest request,
        AlertService svc,
        CancellationToken ct)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.Name)
            || string.IsNullOrWhiteSpace(request.MetricKey)
            || string.IsNullOrWhiteSpace(request.Operator)
            || string.IsNullOrWhiteSpace(request.Channel)
            || string.IsNullOrWhiteSpace(request.Destination))
        {
            return Problem(StatusCodes.Status400BadRequest, "validation", "Name, MetricKey, Operator, Channel and Destination are required.");
        }
        Id<Repository>? repositoryId = null;
        if (!string.IsNullOrWhiteSpace(request.RepositoryId))
        {
            if (!Guid.TryParse(request.RepositoryId, out var parsedRepo))
            {
                return Problem(StatusCodes.Status400BadRequest, "validation", "RepositoryId must be a GUID when provided.");
            }
            repositoryId = Id<Repository>.From(parsedRepo);
        }
        var result = await svc.CreateAsync(
            new CreateAlertRuleCommand(
                Id<Portfolio>.From(portfolioId), repositoryId, request.Name, request.MetricKey,
                request.Operator, request.Threshold, request.MinimumCoverage, request.CooldownHours,
                request.Enabled, request.Channel, request.Destination, request.Secret, DateTimeOffset.UtcNow),
            ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Problem(MapStatus(result.Error!.Value.Code), result.Error.Value.Code, result.Error.Value.Message);
        }
        return Results.Created($"/api/v1/portfolios/{portfolioId}/alert-rules/{result.Value.RuleId}", result.Value);
    }

    private static async Task<IResult> ListAlertRulesAsync(Guid portfolioId, AlertService svc, CancellationToken ct)
    {
        var list = await svc.ListRulesAsync(Id<Portfolio>.From(portfolioId), ct).ConfigureAwait(false);
        return Results.Ok(list);
    }

    private static async Task<IResult> UpdateAlertRuleAsync(
        Guid portfolioId,
        Guid ruleId,
        UpdateAlertRuleRequest request,
        AlertService svc,
        CancellationToken ct)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.Name)
            || string.IsNullOrWhiteSpace(request.MetricKey)
            || string.IsNullOrWhiteSpace(request.Operator)
            || string.IsNullOrWhiteSpace(request.Channel)
            || string.IsNullOrWhiteSpace(request.Destination))
        {
            return Problem(StatusCodes.Status400BadRequest, "validation", "Name, MetricKey, Operator, Channel and Destination are required.");
        }
        var result = await svc.UpdateAsync(
            new UpdateAlertRuleCommand(
                Id<Portfolio>.From(portfolioId), Id<Domain.Alerts.AlertRule>.From(ruleId),
                request.ExpectedVersion, request.Name, request.MetricKey, request.Operator,
                request.Threshold, request.MinimumCoverage, request.CooldownHours, request.Enabled,
                request.Channel, request.Destination, request.Secret, DateTimeOffset.UtcNow),
            ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Problem(MapStatus(result.Error!.Value.Code), result.Error.Value.Code, result.Error.Value.Message);
        }
        return Results.Ok(result.Value);
    }

    private static async Task<IResult> DeleteAlertRuleAsync(
        Guid portfolioId,
        Guid ruleId,
        [FromBody] AlertRuleRevisionRequest request,
        AlertService svc,
        CancellationToken ct)
    {
        if (request is null)
        {
            return Problem(StatusCodes.Status400BadRequest, "validation", "expectedVersion is required.");
        }
        var result = await svc.DeleteAsync(
            Id<Portfolio>.From(portfolioId), Id<Domain.Alerts.AlertRule>.From(ruleId),
            request.ExpectedVersion, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Problem(MapStatus(result.Error!.Value.Code), result.Error.Value.Code, result.Error.Value.Message);
        }
        return Results.Ok(result.Value);
    }

    private static async Task<IResult> EvaluateAlertRuleAsync(
        Guid portfolioId,
        Guid ruleId,
        AlertService svc,
        CancellationToken ct)
    {
        var result = await svc.EvaluateAsync(
            Id<Portfolio>.From(portfolioId), Id<Domain.Alerts.AlertRule>.From(ruleId),
            DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Problem(MapStatus(result.Error!.Value.Code), result.Error.Value.Code, result.Error.Value.Message);
        }
        return Results.Ok(result.Value);
    }

    private static async Task<IResult> ListAlertsAsync(
        Guid portfolioId,
        int? limit,
        AlertService svc,
        CancellationToken ct)
    {
        var list = await svc.ListEvaluationsAsync(
            Id<Portfolio>.From(portfolioId), limit ?? 50, ct).ConfigureAwait(false);
        return Results.Ok(list);
    }

    private static async Task<IResult> CollectSignalsAsync(
        Guid repositoryId,
        CommercialSignalService svc,
        CancellationToken ct)
    {
        var result = await svc.CollectAsync(
            new CollectSignalsCommand(Id<Repository>.From(repositoryId), DateTimeOffset.UtcNow), ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Problem(MapStatus(result.Error!.Value.Code), result.Error.Value.Code, result.Error.Value.Message);
        }
        return Results.Ok(result.Value);
    }

    private static async Task<IResult> ListSignalsAsync(
        Guid repositoryId,
        string? state,
        CommercialSignalService svc,
        CancellationToken ct)
    {
        var list = await svc.ListAsync(Id<Repository>.From(repositoryId), state, ct).ConfigureAwait(false);
        return Results.Ok(list);
    }

    private static async Task<IResult> GetSignalAsync(
        Guid repositoryId,
        Guid signalId,
        CommercialSignalService svc,
        CancellationToken ct)
    {
        var dto = await svc.GetAsync(
            Id<Repository>.From(repositoryId), Id<Domain.Signals.CommercialSignal>.From(signalId), ct).ConfigureAwait(false);
        return dto is null ? Results.NotFound() : Results.Ok(dto);
    }

    private static async Task<IResult> GetSignalReviewsAsync(
        Guid repositoryId,
        Guid signalId,
        CommercialSignalService svc,
        CancellationToken ct)
    {
        var dto = await svc.GetAsync(
            Id<Repository>.From(repositoryId), Id<Domain.Signals.CommercialSignal>.From(signalId), ct).ConfigureAwait(false);
        if (dto is null) return Results.NotFound();
        var reviews = await svc.GetReviewsAsync(
            Id<Repository>.From(repositoryId), Id<Domain.Signals.CommercialSignal>.From(signalId), ct).ConfigureAwait(false);
        return Results.Ok(reviews);
    }

    private static async Task<IResult> ReviewSignalAsync(
        Guid repositoryId,
        Guid signalId,
        ReviewSignalRequest request,
        CommercialSignalService svc,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Decision))
        {
            return Problem(StatusCodes.Status400BadRequest, "validation", "Decision is required.");
        }
        var result = await svc.ReviewAsync(
            new ReviewSignalCommand(
                Id<Repository>.From(repositoryId), Id<Domain.Signals.CommercialSignal>.From(signalId),
                request.ExpectedVersion, request.Decision, request.CorrectedCategory,
                request.Note, request.Reviewer ?? "owner", DateTimeOffset.UtcNow),
            ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Problem(MapStatus(result.Error!.Value.Code), result.Error.Value.Code, result.Error.Value.Message);
        }
        return Results.Ok(result.Value);
    }
}

public sealed record CreatePortfolioRequest(string Name);

public sealed record AddRepositoryRequest(
    string NodeId, string OwnerLogin, string Name, string Visibility, string Role, string? Category, string Lifecycle);

public sealed record UpdateMembershipRequest(string? Category, string Lifecycle);

public sealed record ScoreFactorRequest(string Name, double Weight, bool Enabled);

public sealed record ScoreConfigurationRequest(IReadOnlyList<ScoreFactorRequest> Factors);

public sealed record ScoreConfigurationDto(int Version, IReadOnlyList<ScoreFactor> Factors, bool IsDefault);

public sealed record CreatePackageAssociationRequest(
    string Provider,
    string Coordinate,
    string? DefaultUnit,
    string? DefaultWindow);

public sealed record UpdatePackageAssociationRequest(
    string DefaultUnit,
    string DefaultWindow);

public sealed record CreateDecisionRequest(
    string? RepositoryId,
    string DecisionType,
    string DecisionDate,
    string Rationale,
    string? ReviewDate,
    string? IdempotencyKey,
    string? Note,
    string? ActorId,
    IReadOnlyList<CreateDecisionEvidenceRequest>? Evidence);

public sealed record CreateDecisionEvidenceRequest(
    string? Kind,
    string? ReferenceId,
    string? Label);

public sealed record UpdateDecisionRequest(
    int ExpectedRevision,
    string DecisionType,
    string DecisionDate,
    string Rationale,
    string? ReviewDate,
    string? Note,
    string? ActorId,
    IReadOnlyList<CreateDecisionEvidenceRequest>? Evidence);

public sealed record DecisionRevisionRequest(
    int ExpectedRevision,
    string? Note,
    string? ActorId);

public sealed record CreateAlertRuleRequest(
    string? RepositoryId,
    string Name,
    string MetricKey,
    string Operator,
    double Threshold,
    double MinimumCoverage,
    int CooldownHours,
    bool Enabled,
    string Channel,
    string Destination,
    string? Secret);

public sealed record UpdateAlertRuleRequest(
    int ExpectedVersion,
    string Name,
    string MetricKey,
    string Operator,
    double Threshold,
    double MinimumCoverage,
    int CooldownHours,
    bool Enabled,
    string Channel,
    string Destination,
    string? Secret);

public sealed record AlertRuleRevisionRequest(int ExpectedVersion);

public sealed record ReviewSignalRequest(
    int ExpectedVersion,
    string Decision,
    string? CorrectedCategory,
    string? Note,
    string? Reviewer);
