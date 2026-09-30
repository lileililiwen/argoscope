using System.Text.Json;
using System.Text.Json.Serialization;
using Argoscope.Application.Analytics;
using Argoscope.Application.Collection;
using Argoscope.Application.Packages;
using Argoscope.Application.Portfolios;
using Argoscope.Application.Ranking;
using Argoscope.Domain.Common;
using Argoscope.Domain.Memberships;
using Argoscope.Domain.Packages;
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

        // Package adoption: owner-managed associations, on-demand
        // collection, and the read-side adoption report.
        v1.MapGet("/repositories/{repositoryId:guid}/packages", (Guid repositoryId, PackageAssociationService svc, CancellationToken ct) => ListPackageAssociationsAsync(repositoryId, svc, ct));
        v1.MapPost("/repositories/{repositoryId:guid}/packages", (Guid repositoryId, CreatePackageAssociationRequest request, PackageAssociationService svc, CancellationToken ct) => CreatePackageAssociationAsync(repositoryId, request, svc, ct));
        v1.MapPut("/repositories/{repositoryId:guid}/packages/{associationId:guid}", (Guid repositoryId, Guid associationId, UpdatePackageAssociationRequest request, PackageAssociationService svc, CancellationToken ct) => UpdatePackageAssociationAsync(repositoryId, associationId, request, svc, ct));
        v1.MapDelete("/repositories/{repositoryId:guid}/packages/{associationId:guid}", (Guid repositoryId, Guid associationId, PackageAssociationService svc, CancellationToken ct) => RemovePackageAssociationAsync(repositoryId, associationId, svc, ct));
        v1.MapPost("/repositories/{repositoryId:guid}/packages/{associationId:guid}/collect", (Guid repositoryId, Guid associationId, PackageCollectionService svc, PackageAssociationService assocSvc, CancellationToken ct) => CollectPackageNowAsync(repositoryId, associationId, svc, assocSvc, ct));
        v1.MapGet("/repositories/{repositoryId:guid}/adoption", (Guid repositoryId, PackageAdoptionService svc, CancellationToken ct) => GetAdoptionAsync(repositoryId, svc, ct));

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
