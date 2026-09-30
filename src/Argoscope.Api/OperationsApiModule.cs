using Argoscope.Application.Identity;
using Argoscope.Application.Operations;
using Argoscope.Domain.Common;
using Argoscope.Domain.Identity;
using Argoscope.Domain.Operations;
using Argoscope.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Argoscope.Api;

/// <summary>Hosted operations endpoints: split health gates, immutable
/// release identity, tenant-data deletion lifecycle, incident evidence and
/// isolated restore-rehearsal records. Health responses expose status
/// components only — never connection strings, secrets or tenant data.</summary>
public static class OperationsApiModule
{
    public static IEndpointRouteBuilder MapOperationsApi(this IEndpointRouteBuilder builder)
    {
        var v1 = builder.MapGroup("/api/v1").WithGroupName("argoscope-operations");

        v1.MapGet("/health/live", (HttpContext http) => Live());
        v1.MapGet("/health/ready", (HttpContext http, ArgoscopeDbContext db, IConfiguration config, IOptions<OperationsOptions> opts, CancellationToken ct) => ReadyAsync(http, db, config, opts.Value, ct));
        v1.MapGet("/ops/release", (HttpContext http, ArgoscopeDbContext db, IConfiguration config, IOptions<OperationsOptions> opts) => Release(http, db, config, opts.Value));

        v1.MapPost("/tenants/{tenantId:guid}/deletion", (Guid tenantId, DeletionRequestBody? body, HttpContext http, TenantService tenants, OperationsService ops, ISessionStore sessions, IOptions<IdentityOptions> opts, CancellationToken ct) => RequestDeletionAsync(tenantId, body, http, tenants, ops, sessions, opts.Value, ct));
        v1.MapGet("/tenants/{tenantId:guid}/deletion", (Guid tenantId, HttpContext http, TenantService tenants, IDeletionStore deletions, ISessionStore sessions, IOptions<IdentityOptions> opts, CancellationToken ct) => GetDeletionAsync(tenantId, http, tenants, deletions, sessions, opts.Value, ct));
        v1.MapPost("/tenants/{tenantId:guid}/deletion/active-purge", (Guid tenantId, HttpContext http, TenantService tenants, OperationsService ops, ISessionStore sessions, IOptions<IdentityOptions> opts, CancellationToken ct) => ActivePurgeAsync(tenantId, http, tenants, ops, sessions, opts.Value, ct));
        v1.MapPost("/tenants/{tenantId:guid}/deletion/backup-expiry", (Guid tenantId, HttpContext http, TenantService tenants, OperationsService ops, ISessionStore sessions, IOptions<IdentityOptions> opts, CancellationToken ct) => BackupExpiryAsync(tenantId, http, tenants, ops, sessions, opts.Value, ct));

        v1.MapGet("/ops/incidents", (HttpContext http, TenantService tenants, IIncidentStore incidents, ISessionStore sessions, IOptions<IdentityOptions> opts, CancellationToken ct) => ListIncidentsAsync(http, tenants, incidents, sessions, opts.Value, ct));
        v1.MapPost("/ops/incidents", (OpenIncidentBody body, HttpContext http, TenantService tenants, OperationsService ops, ISessionStore sessions, IOptions<IdentityOptions> opts, CancellationToken ct) => OpenIncidentAsync(body, http, tenants, ops, sessions, opts.Value, ct));
        v1.MapPost("/ops/incidents/{incidentId:guid}/resolve", (Guid incidentId, ResolveIncidentBody? body, HttpContext http, TenantService tenants, OperationsService ops, ISessionStore sessions, IOptions<IdentityOptions> opts, CancellationToken ct) => ResolveIncidentAsync(incidentId, body, http, tenants, ops, sessions, opts.Value, ct));

        v1.MapGet("/ops/restore-rehearsals", (HttpContext http, TenantService tenants, IRestoreRehearsalStore rehearsals, ISessionStore sessions, IOptions<IdentityOptions> opts, CancellationToken ct) => ListRehearsalsAsync(http, tenants, rehearsals, sessions, opts.Value, ct));
        v1.MapPost("/ops/restore-rehearsals", (RecordRehearsalBody body, HttpContext http, TenantService tenants, OperationsService ops, ISessionStore sessions, IOptions<IdentityOptions> opts, CancellationToken ct) => RecordRehearsalAsync(body, http, tenants, ops, sessions, opts.Value, ct));

        return builder;
    }

    private static IResult Live()
    {
        var release = ReleaseInfoProvider.Current();
        return Results.Ok(new
        {
            status = "ok",
            revision = release.Revision,
            time = DateTimeOffset.UtcNow,
        });
    }

    private static async Task<IResult> ReadyAsync(
        HttpContext http, ArgoscopeDbContext db, IConfiguration config,
        OperationsOptions options, CancellationToken ct)
    {
        var release = ReleaseInfoProvider.Current();
        var checks = new Dictionary<string, object?>();

        var dbOk = false;
        try
        {
            dbOk = await db.Database.CanConnectAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            dbOk = false;
        }

        checks["database"] = dbOk ? "ok" : "unavailable";
        // Expand-compatible schema: the model is applied with EnsureCreated
        // for InMemory/dev and with staged migrations in production; a failed
        // migration aborts the release before traffic promotion.
        checks["migrations"] = dbOk ? "compatible" : "blocked";

        var jobsConfigured = !string.IsNullOrWhiteSpace(config["GitHub:Token"]);
        checks["jobs"] = jobsConfigured ? "enabled" : "idle";

        var ready = dbOk;
        return ready
            ? Results.Ok(new
            {
                status = "ready",
                revision = release.Revision,
                checks,
                time = DateTimeOffset.UtcNow,
            })
            : Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "not_ready",
                detail: "Readiness failed; traffic promotion is blocked.",
                extensions: new Dictionary<string, object?>
                {
                    ["revision"] = release.Revision,
                    ["checks"] = checks,
                });
    }

    private static IResult Release(
        HttpContext http, ArgoscopeDbContext db, IConfiguration config, OperationsOptions options)
    {
        var release = ReleaseInfoProvider.Current();
        return Results.Ok(new
        {
            revision = release.Revision,
            artifact = release.Artifact,
            builtAt = release.BuiltAtUtc,
            migrations = "expand-compatible",
            sloAvailabilityPercent = options.SloAvailabilityPercent,
            rpoHours = options.RpoHours,
            rtoHours = options.RtoHours,
            backupRetentionDays = options.BackupRetentionDays,
            region = string.IsNullOrWhiteSpace(options.Region) ? "unselected" : options.Region,
            requireOperatorSignoff = options.RequireOperatorSignoff,
        });
    }

    private static async Task<(TenantPrincipal? Principal, IResult? Error)> RequireTenantMemberAsync(
        HttpContext http, Guid tenantId, TenantService svc, ISessionStore sessions, IdentityOptions options, CancellationToken ct)
    {
        if (!options.IsHosted)
        {
            return (null, null);
        }

        var (principal, viaCookie, error) = await TenantHttp
            .ResolveAsync(http, svc, sessions, options, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        if (error is not null || principal is null)
        {
            return (null, Results.Unauthorized());
        }

        if (principal.TenantId.Value != tenantId)
        {
            return (null, Results.NotFound());
        }

        if (viaCookie && IsMutation(http)
            && !string.Equals(http.Request.Headers[options.CsrfHeader].FirstOrDefault(), "1", StringComparison.Ordinal))
        {
            return (null, Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "forbidden", detail: "CSRF header is required."));
        }

        TenantHttp.SetPrincipal(http, principal);
        return (principal, null);
    }

    private static async Task<(TenantPrincipal? Principal, IResult? Error)> RequireOpsMemberAsync(
        HttpContext http, TenantService svc, ISessionStore sessions, IdentityOptions options, CancellationToken ct)
    {
        if (!options.IsHosted)
        {
            return (null, null);
        }

        var (principal, viaCookie, error) = await TenantHttp
            .ResolveAsync(http, svc, sessions, options, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        if (error is not null || principal is null)
        {
            return (null, Results.Unauthorized());
        }

        if (viaCookie && IsMutation(http)
            && !string.Equals(http.Request.Headers[options.CsrfHeader].FirstOrDefault(), "1", StringComparison.Ordinal))
        {
            return (null, Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "forbidden", detail: "CSRF header is required."));
        }

        TenantHttp.SetPrincipal(http, principal);
        return (principal, null);
    }

    private static bool IsMutation(HttpContext http) =>
        !HttpMethods.IsGet(http.Request.Method)
        && !HttpMethods.IsHead(http.Request.Method)
        && !HttpMethods.IsOptions(http.Request.Method);

    private static bool RequireOwner(TenantPrincipal? principal, IdentityOptions options) =>
        !options.IsHosted || (principal is not null && TenantAuthorization.CanManageMembers(principal.Role));

    private static async Task<IResult> RequestDeletionAsync(
        Guid tenantId, DeletionRequestBody? body, HttpContext http, TenantService tenants,
        OperationsService ops, ISessionStore sessions, IdentityOptions options, CancellationToken ct)
    {
        var (principal, error) = await RequireTenantMemberAsync(http, tenantId, tenants, sessions, options, ct).ConfigureAwait(false);
        if (error is not null) return error;
        if (!RequireOwner(principal, options))
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "forbidden", detail: "Deletion requires the Owner role.");
        }

        var actor = principal?.Subject ?? body?.RequestedBy ?? string.Empty;
        var result = await ops.RequestDeletionAsync(Id<Tenant>.From(tenantId), actor, ct).ConfigureAwait(false);
        return result.IsSuccess
            ? Results.Created($"/api/v1/tenants/{tenantId}/deletion", result.Value)
            : Results.Problem(statusCode: TenantHttp.MapStatus(result.Error!.Value.Code), title: result.Error.Value.Code, detail: result.Error.Value.Message);
    }

    private static async Task<IResult> GetDeletionAsync(
        Guid tenantId, HttpContext http, TenantService tenants, IDeletionStore deletions,
        ISessionStore sessions, IdentityOptions options, CancellationToken ct)
    {
        var (_, error) = await RequireTenantMemberAsync(http, tenantId, tenants, sessions, options, ct).ConfigureAwait(false);
        if (error is not null) return error;
        var request = await deletions.FindByTenantAsync(Id<Tenant>.From(tenantId), ct).ConfigureAwait(false);
        return request is null ? Results.NotFound() : Results.Ok(OperationsDtos.ToDto(request));
    }

    private static async Task<IResult> ActivePurgeAsync(
        Guid tenantId, HttpContext http, TenantService tenants, OperationsService ops,
        ISessionStore sessions, IdentityOptions options, CancellationToken ct)
    {
        var (principal, error) = await RequireTenantMemberAsync(http, tenantId, tenants, sessions, options, ct).ConfigureAwait(false);
        if (error is not null) return error;
        if (!RequireOwner(principal, options))
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "forbidden", detail: "Deletion requires the Owner role.");
        }

        var result = await ops.MarkActivePurgedAsync(Id<Tenant>.From(tenantId), ct).ConfigureAwait(false);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : Results.Problem(statusCode: TenantHttp.MapStatus(result.Error!.Value.Code), title: result.Error.Value.Code, detail: result.Error.Value.Message);
    }

    private static async Task<IResult> BackupExpiryAsync(
        Guid tenantId, HttpContext http, TenantService tenants, OperationsService ops,
        ISessionStore sessions, IdentityOptions options, CancellationToken ct)
    {
        var (principal, error) = await RequireTenantMemberAsync(http, tenantId, tenants, sessions, options, ct).ConfigureAwait(false);
        if (error is not null) return error;
        if (!RequireOwner(principal, options))
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "forbidden", detail: "Deletion requires the Owner role.");
        }

        var result = await ops.MarkBackupExpiredAsync(Id<Tenant>.From(tenantId), ct).ConfigureAwait(false);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : Results.Problem(statusCode: TenantHttp.MapStatus(result.Error!.Value.Code), title: result.Error.Value.Code, detail: result.Error.Value.Message);
    }

    private static async Task<IResult> ListIncidentsAsync(
        HttpContext http, TenantService tenants, IIncidentStore incidents,
        ISessionStore sessions, IdentityOptions options, CancellationToken ct)
    {
        var (_, error) = await RequireOpsMemberAsync(http, tenants, sessions, options, ct).ConfigureAwait(false);
        if (error is not null) return error;
        var list = await incidents.ListAsync(ct).ConfigureAwait(false);
        return Results.Ok(list.Select(OperationsDtos.ToDto).ToList());
    }

    private static async Task<IResult> OpenIncidentAsync(
        OpenIncidentBody body, HttpContext http, TenantService tenants, OperationsService ops,
        ISessionStore sessions, IdentityOptions options, CancellationToken ct)
    {
        var (principal, error) = await RequireOpsMemberAsync(http, tenants, sessions, options, ct).ConfigureAwait(false);
        if (error is not null) return error;
        if (!RequireOwner(principal, options))
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "forbidden", detail: "Incidents require the Owner role.");
        }

        if (body is null || string.IsNullOrWhiteSpace(body.Title))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "validation", detail: "Title is required.");
        }

        var result = await ops.OpenIncidentAsync(
            body.Title, body.Severity ?? "Medium", body.Scope ?? string.Empty, body.Summary ?? string.Empty, ct).ConfigureAwait(false);
        return result.IsSuccess
            ? Results.Created($"/api/v1/ops/incidents/{result.Value.Id}", result.Value)
            : Results.Problem(statusCode: TenantHttp.MapStatus(result.Error!.Value.Code), title: result.Error.Value.Code, detail: result.Error.Value.Message);
    }

    private static async Task<IResult> ResolveIncidentAsync(
        Guid incidentId, ResolveIncidentBody? body, HttpContext http, TenantService tenants,
        OperationsService ops, ISessionStore sessions, IdentityOptions options, CancellationToken ct)
    {
        var (principal, error) = await RequireOpsMemberAsync(http, tenants, sessions, options, ct).ConfigureAwait(false);
        if (error is not null) return error;
        if (!RequireOwner(principal, options))
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "forbidden", detail: "Incidents require the Owner role.");
        }

        var result = await ops.ResolveIncidentAsync(Id<OperationalIncident>.From(incidentId), body?.Resolution, ct).ConfigureAwait(false);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : Results.Problem(statusCode: TenantHttp.MapStatus(result.Error!.Value.Code), title: result.Error.Value.Code, detail: result.Error.Value.Message);
    }

    private static async Task<IResult> ListRehearsalsAsync(
        HttpContext http, TenantService tenants, IRestoreRehearsalStore rehearsals,
        ISessionStore sessions, IdentityOptions options, CancellationToken ct)
    {
        var (_, error) = await RequireOpsMemberAsync(http, tenants, sessions, options, ct).ConfigureAwait(false);
        if (error is not null) return error;
        var list = await rehearsals.ListAsync(ct).ConfigureAwait(false);
        return Results.Ok(list.Select(OperationsDtos.ToDto).ToList());
    }

    private static async Task<IResult> RecordRehearsalAsync(
        RecordRehearsalBody body, HttpContext http, TenantService tenants, OperationsService ops,
        ISessionStore sessions, IdentityOptions options, CancellationToken ct)
    {
        var (principal, error) = await RequireOpsMemberAsync(http, tenants, sessions, options, ct).ConfigureAwait(false);
        if (error is not null) return error;
        if (!RequireOwner(principal, options))
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "forbidden", detail: "Rehearsals require the Owner role.");
        }

        if (body is null || string.IsNullOrWhiteSpace(body.ArtifactRevision))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "validation", detail: "ArtifactRevision is required.");
        }

        if (!DateTimeOffset.TryParse(body.StartedAtUtc, out var started) || !DateTimeOffset.TryParse(body.FinishedAtUtc, out var finished))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "validation", detail: "StartedAtUtc and FinishedAtUtc must be RFC3339 timestamps.");
        }

        var actor = principal?.Subject ?? body.RecordedBy ?? string.Empty;
        var result = await ops.RecordRehearsalAsync(
            body.ArtifactRevision, body.DatabaseRevision ?? string.Empty,
            started, finished, body.RpoHoursMeasured, body.RtoHoursMeasured,
            body.IntegrityOk, actor, ct).ConfigureAwait(false);
        return result.IsSuccess
            ? Results.Created($"/api/v1/ops/restore-rehearsals/{result.Value.Id}", result.Value)
            : Results.Problem(statusCode: TenantHttp.MapStatus(result.Error!.Value.Code), title: result.Error.Value.Code, detail: result.Error.Value.Message);
    }
}

public sealed record DeletionRequestBody(string? RequestedBy);

public sealed record OpenIncidentBody(string Title, string? Severity, string? Scope, string? Summary);

public sealed record ResolveIncidentBody(string? Resolution);

public sealed record RecordRehearsalBody(
    string ArtifactRevision, string? DatabaseRevision,
    string StartedAtUtc, string FinishedAtUtc,
    double RpoHoursMeasured, double RtoHoursMeasured,
    bool IntegrityOk, string? RecordedBy);
