using System.Text.Json;
using System.Text.Json.Serialization;
using Argoscope.Application.Analytics;
using Argoscope.Application.Collection;
using Argoscope.Application.Portfolios;
using Argoscope.Application.Ranking;
using Argoscope.Domain.Common;
using Argoscope.Domain.Memberships;
using Argoscope.Domain.Portfolios;
using Argoscope.Domain.Repositories;
using Argoscope.Domain.Scores;
using Argoscope.Domain.Snapshots;
using Argoscope.GitHub;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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
        Converters = { new JsonStringEnumConverter() },
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

        v1.MapPost("/portfolios", (CreatePortfolioRequest request, PortfolioService svc, CancellationToken ct) => CreatePortfolioAsync(request, svc, ct));
        v1.MapGet("/portfolios", (IPortfolioRepository repo, CancellationToken ct) => ListPortfoliosAsync(repo, ct));
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

        return builder;
    }

    private static async Task<IResult> CreatePortfolioAsync(CreatePortfolioRequest request, PortfolioService svc, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.Name))
        {
            return Problem(StatusCodes.Status400BadRequest, "validation", "Name is required.");
        }
        var result = await svc.CreateAsync(new CreatePortfolioCommand(request.Name, DateTimeOffset.UtcNow), ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Problem(StatusCodes.Status400BadRequest, result.Error!.Value.Code, result.Error.Value.Message);
        }
        return Results.Created($"/api/v1/portfolios/{result.Value.Id}", result.Value);
    }

    private static async Task<IResult> ListPortfoliosAsync(IPortfolioRepository portfolioRepository, CancellationToken ct)
    {
        var ids = await portfolioRepository.ListAllAsync(ct).ConfigureAwait(false);
        var list = new List<PortfolioDto>();
        foreach (var id in ids)
        {
            var dto = await GetPortfolioInternalAsync(id, portfolioRepository, ct).ConfigureAwait(false);
            if (dto is not null) list.Add(dto);
        }
        return Results.Ok(list);
    }

    private static async Task<PortfolioDto?> GetPortfolioInternalAsync(Id<Portfolio> id, IPortfolioRepository repo, CancellationToken ct)
    {
        var p = await repo.FindAsync(id, ct).ConfigureAwait(false);
        return p is null ? null : new PortfolioDto(p.Id.Value, p.Name, p.CreatedAtUtc, p.UpdatedAtUtc);
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

    private static IResult Problem(int statusCode, string code, string detail) =>
        Results.Problem(statusCode: statusCode, title: code, detail: detail);
}

public sealed record CreatePortfolioRequest(string Name);

public sealed record AddRepositoryRequest(
    string NodeId, string OwnerLogin, string Name, string Visibility, string Role, string? Category, string Lifecycle);

public sealed record UpdateMembershipRequest(string? Category, string Lifecycle);

public sealed record ScoreFactorRequest(string Name, double Weight, bool Enabled);

public sealed record ScoreConfigurationRequest(IReadOnlyList<ScoreFactorRequest> Factors);

public sealed record ScoreConfigurationDto(int Version, IReadOnlyList<ScoreFactor> Factors, bool IsDefault);
