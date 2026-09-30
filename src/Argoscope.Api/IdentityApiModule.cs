using Argoscope.Application.Billing;
using Argoscope.Application.Identity;
using Argoscope.Domain.Common;
using Argoscope.Domain.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Argoscope.Api;

/// <summary>Per-request tenant principal plumbing shared by the authorization
/// filter and the identity handlers.</summary>
public static class TenantHttp
{
    public const string PrincipalKey = "Argoscope.TenantPrincipal";

    public static TenantPrincipal? GetPrincipal(HttpContext context) =>
        context.Items.TryGetValue(PrincipalKey, out var value) ? value as TenantPrincipal : null;

    public static void SetPrincipal(HttpContext context, TenantPrincipal principal) =>
        context.Items[PrincipalKey] = principal;

    public static async Task<(TenantPrincipal? Principal, bool ViaCookie, IResult? Error)> ResolveAsync(
        HttpContext context,
        TenantService tenants,
        ISessionStore sessions,
        IdentityOptions options,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (context.Request.Headers.TryGetValue(options.TenantHeader, out var tenantValues)
            && Guid.TryParse(tenantValues.FirstOrDefault(), out var tenantId)
            && context.Request.Headers.TryGetValue(options.UserHeader, out var userValues)
            && !string.IsNullOrWhiteSpace(userValues.FirstOrDefault()))
        {
            var principal = await tenants.ResolvePrincipalAsync(
                Id<Tenant>.From(tenantId), userValues.ToString(), viaCookie: false, cancellationToken).ConfigureAwait(false);
            return principal is null
                ? (null, false, Results.Unauthorized())
                : (principal, false, null);
        }

        if (context.Request.Cookies.TryGetValue(options.SessionCookie, out var token)
            && !string.IsNullOrEmpty(token))
        {
            var session = await sessions.FindAsync(token, now, cancellationToken).ConfigureAwait(false);
            if (session is null)
            {
                return (null, true, Results.Unauthorized());
            }

            var principal = await tenants.ResolvePrincipalAsync(
                session.TenantId, session.Subject, viaCookie: true, cancellationToken).ConfigureAwait(false);
            return principal is null
                ? (null, true, Results.Unauthorized())
                : (principal, true, null);
        }

        return (null, false, Results.Unauthorized());
    }

    public static void AppendSessionCookie(
        HttpContext context, IdentityOptions options, string token, DateTimeOffset expiresAtUtc)
    {
        context.Response.Cookies.Append(options.SessionCookie, token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = context.Request.IsHttps,
            Expires = expiresAtUtc,
            Path = "/",
        });
    }

    public static int MapStatus(string code) => code switch
    {
        "not_found" => StatusCodes.Status404NotFound,
        "conflict" => StatusCodes.Status409Conflict,
        "unauthorized" => StatusCodes.Status401Unauthorized,
        "forbidden" => StatusCodes.Status403Forbidden,
        "payment_required" => StatusCodes.Status402PaymentRequired,
        "unavailable" => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status400BadRequest,
    };
}

/// <summary>
/// Enforces tenant isolation for every portfolio/resource endpoint in the v1
/// group. Identity bootstrap routes (/tenants, /auth, /invites) enforce their
/// own rules in <see cref="IdentityApiModule"/>. In SingleOwner mode the
/// filter passes through, preserving legacy open local behavior.
/// </summary>
public sealed class TenantAuthorizationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var options = http.RequestServices.GetRequiredService<IOptions<IdentityOptions>>().Value;
        if (!options.IsHosted)
        {
            return await next(context).ConfigureAwait(false);
        }

        var tenants = http.RequestServices.GetRequiredService<TenantService>();
        var sessions = http.RequestServices.GetRequiredService<ISessionStore>();
        var now = DateTimeOffset.UtcNow;

        var (principal, viaCookie, error) = await TenantHttp
            .ResolveAsync(http, tenants, sessions, options, now, http.RequestAborted).ConfigureAwait(false);
        if (error is not null || principal is null)
        {
            return Results.Unauthorized();
        }

        TenantHttp.SetPrincipal(http, principal);

        // Cookie-authenticated mutations require the CSRF header.
        if (viaCookie
            && !HttpMethods.IsGet(http.Request.Method)
            && !HttpMethods.IsHead(http.Request.Method)
            && !HttpMethods.IsOptions(http.Request.Method)
            && !string.Equals(http.Request.Headers[options.CsrfHeader].FirstOrDefault(), "1", StringComparison.Ordinal))
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "forbidden", detail: "CSRF header is required.");
        }

        var portfolios = http.RequestServices.GetRequiredService<Application.Collection.IPortfolioRepository>();
        var memberships = http.RequestServices.GetRequiredService<Application.Collection.IMembershipStore>();

        var needsWrite = !HttpMethods.IsGet(http.Request.Method)
            && !HttpMethods.IsHead(http.Request.Method)
            && !HttpMethods.IsOptions(http.Request.Method);

        if (http.Request.RouteValues.TryGetValue("portfolioId", out var portfolioValue)
            && Guid.TryParse(portfolioValue?.ToString(), out var portfolioId))
        {
            var portfolio = await portfolios
                .FindAsync(Id<Domain.Portfolios.Portfolio>.From(portfolioId), http.RequestAborted).ConfigureAwait(false);
            if (portfolio is null)
            {
                return await next(context).ConfigureAwait(false);
            }

            if (portfolio.TenantId is null)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "conflict", detail: "Portfolio is not assigned to a tenant; hosted access is disabled until migration.");
            }

            if (portfolio.TenantId != principal.TenantId)
            {
                return Results.NotFound();
            }

            if (http.Request.RouteValues.TryGetValue("repositoryId", out var repoValue)
                && Guid.TryParse(repoValue?.ToString(), out var repositoryId))
            {
                var membership = await memberships.FindByRepositoryAsync(
                    Id<Domain.Portfolios.Portfolio>.From(portfolioId),
                    Id<Domain.Repositories.Repository>.From(repositoryId),
                    http.RequestAborted).ConfigureAwait(false);
                if (membership is null)
                {
                    return Results.NotFound();
                }
            }

            if (needsWrite && !TenantAuthorization.CanMutate(principal.Role))
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "forbidden", detail: "Viewer cannot mutate portfolio data.");
            }

            var billingDenial = await CheckBillingAsync(
                http, portfolios, principal, needsWrite, now, http.RequestAborted).ConfigureAwait(false);
            if (billingDenial is not null)
            {
                return billingDenial;
            }

            return await next(context).ConfigureAwait(false);
        }

        if (http.Request.RouteValues.TryGetValue("repositoryId", out var repositoryValue)
            && Guid.TryParse(repositoryValue?.ToString(), out var repoId))
        {
            // Repository children are mediated through portfolio membership:
            // the caller must hold at least one same-tenant membership for
            // the repository, otherwise the resource is not found.
            var portfolioIds = await portfolios.ListAllAsync(http.RequestAborted).ConfigureAwait(false);
            var shared = false;
            var owned = false;
            foreach (var pid in portfolioIds)
            {
                var membership = await memberships.FindByRepositoryAsync(
                    pid, Id<Domain.Repositories.Repository>.From(repoId), http.RequestAborted).ConfigureAwait(false);
                if (membership is null) continue;
                shared = true;
                var portfolio = await portfolios.FindAsync(pid, http.RequestAborted).ConfigureAwait(false);
                if (portfolio?.TenantId is null)
                {
                    return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "conflict", detail: "Repository portfolio is not assigned to a tenant; hosted access is disabled until migration.");
                }

                if (portfolio.TenantId == principal.TenantId)
                {
                    owned = true;
                }
            }

            if (!shared)
            {
                return await next(context).ConfigureAwait(false);
            }

            if (!owned)
            {
                return Results.NotFound();
            }

            if (needsWrite && !TenantAuthorization.CanMutate(principal.Role))
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "forbidden", detail: "Viewer cannot mutate portfolio data.");
            }

            var billingDenial = await CheckBillingAsync(
                http, portfolios, principal, needsWrite, now, http.RequestAborted).ConfigureAwait(false);
            if (billingDenial is not null)
            {
                return billingDenial;
            }

            return await next(context).ConfigureAwait(false);
        }

        if (needsWrite)
        {
            var fallthroughDenial = await CheckBillingAsync(
                http, portfolios, principal, needsWrite, now, http.RequestAborted).ConfigureAwait(false);
            if (fallthroughDenial is not null)
            {
                return fallthroughDenial;
            }
        }

        return await next(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Entitlement enforcement for hosted mutations. Runs after tenant
    /// ownership and role checks so 401/404/409/403 semantics are unchanged:
    /// read-only accounts get a 402-shaped denial with a stable code and
    /// upgrade URL, and portfolio creation is bounded by the plan limit. A
    /// tenant without a subscription row is unconfigured billing and is
    /// allowed through.
    /// </summary>
    private static async Task<IResult?> CheckBillingAsync(
        HttpContext http,
        Application.Collection.IPortfolioRepository portfolios,
        TenantPrincipal principal,
        bool needsWrite,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!needsWrite) return null;

        var billing = http.RequestServices.GetRequiredService<BillingService>();
        if (!billing.BillingEnforced) return null;

        var (version, entitlements) = await billing.GetEntitlementsAsync(
            principal.TenantId, now, cancellationToken).ConfigureAwait(false);
        if (entitlements is null) return null;

        var billingOptions = http.RequestServices.GetRequiredService<IOptions<BillingOptions>>().Value;
        if (!entitlements.CanMutate)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status402PaymentRequired,
                title: "payment_required",
                detail: entitlements.ReadOnlyReason ?? "Subscription is not active.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "billing_read_only",
                    ["upgradeUrl"] = billingOptions.UpgradeUrl,
                });
        }

        if (HttpMethods.IsPost(http.Request.Method)
            && http.Request.Path.Value?.EndsWith("/portfolios", StringComparison.OrdinalIgnoreCase) == true)
        {
            var count = 0;
            var portfolioIds = await portfolios.ListAllAsync(cancellationToken).ConfigureAwait(false);
            foreach (var pid in portfolioIds)
            {
                var portfolio = await portfolios.FindAsync(pid, cancellationToken).ConfigureAwait(false);
                if (portfolio?.TenantId == principal.TenantId)
                {
                    count++;
                }
            }

            if (count >= entitlements.MaxPortfolios)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status402PaymentRequired,
                    title: "payment_required",
                    detail: $"Plan '{entitlements.PlanId}' allows {entitlements.MaxPortfolios} portfolios.",
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "billing_limit_exceeded",
                        ["upgradeUrl"] = billingOptions.UpgradeUrl,
                    });
            }
        }

        return null;
    }
}

/// <summary>Identity and tenant membership endpoints (owner-managed).</summary>
public static class IdentityApiModule
{
    public static IEndpointRouteBuilder MapIdentityApi(this IEndpointRouteBuilder builder)
    {
        var v1 = builder.MapGroup("/api/v1").WithGroupName("argoscope-identity");

        v1.MapPost("/tenants", (CreateTenantRequest request, HttpContext http, TenantService svc, BillingService billing, ISessionStore sessions, IOptions<IdentityOptions> opts, CancellationToken ct) => CreateTenantAsync(request, http, svc, billing, sessions, opts.Value, ct));
        v1.MapGet("/tenants/{tenantId:guid}", (Guid tenantId, HttpContext http, TenantService svc, ISessionStore sessions, IOptions<IdentityOptions> opts, CancellationToken ct) => GetTenantAsync(tenantId, http, svc, sessions, opts.Value, ct));
        v1.MapGet("/tenants/{tenantId:guid}/members", (Guid tenantId, HttpContext http, TenantService svc, ISessionStore sessions, IOptions<IdentityOptions> opts, CancellationToken ct) => ListMembersAsync(tenantId, http, svc, sessions, opts.Value, ct));
        v1.MapPost("/tenants/{tenantId:guid}/invites", (Guid tenantId, InviteMemberRequest request, HttpContext http, TenantService svc, ISessionStore sessions, IOptions<IdentityOptions> opts, CancellationToken ct) => InviteAsync(tenantId, request, http, svc, sessions, opts.Value, ct));
        v1.MapPost("/invites/accept", (AcceptInviteRequest request, HttpContext http, TenantService svc, ISessionStore sessions, IOptions<IdentityOptions> opts, CancellationToken ct) => AcceptInviteAsync(request, http, svc, sessions, opts.Value, ct));
        v1.MapPut("/tenants/{tenantId:guid}/members/{membershipId:guid}", (Guid tenantId, Guid membershipId, ChangeRoleRequest request, HttpContext http, TenantService svc, ISessionStore sessions, IOptions<IdentityOptions> opts, CancellationToken ct) => ChangeRoleAsync(tenantId, membershipId, request, http, svc, sessions, opts.Value, ct));
        v1.MapPost("/tenants/{tenantId:guid}/members/{membershipId:guid}/revoke", (Guid tenantId, Guid membershipId, HttpContext http, TenantService svc, ISessionStore sessions, IOptions<IdentityOptions> opts, CancellationToken ct) => RevokeAsync(tenantId, membershipId, http, svc, sessions, opts.Value, ct));
        v1.MapPost("/auth/oidc/callback", (OidcCallbackRequest request, HttpContext http, TenantService svc, ISessionStore sessions, IOptions<IdentityOptions> opts, CancellationToken ct) => OidcCallbackAsync(request, http, svc, sessions, opts.Value, ct));
        v1.MapPost("/tenants/{tenantId:guid}/migrate", (Guid tenantId, HttpContext http, TenantMigrationService migration, TenantService svc, ISessionStore sessions, IOptions<IdentityOptions> opts, CancellationToken ct) => MigrateAsync(tenantId, http, migration, svc, sessions, opts.Value, ct));

        return builder;
    }

    private static async Task<(TenantPrincipal? Principal, IResult? Error)> RequireTenantMemberAsync(
        HttpContext http, Guid tenantId, TenantService svc, ISessionStore sessions, IdentityOptions options, CancellationToken ct)
    {
        if (!options.IsHosted)
        {
            return (null, null);
        }

        var (principal, _, error) = await TenantHttp
            .ResolveAsync(http, svc, sessions, options, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        if (error is not null || principal is null)
        {
            return (null, Results.Unauthorized());
        }

        if (principal.TenantId.Value != tenantId)
        {
            // No disclosure across tenants.
            return (null, Results.NotFound());
        }

        TenantHttp.SetPrincipal(http, principal);
        return (principal, null);
    }

    private static async Task<IResult> CreateTenantAsync(
        CreateTenantRequest request, HttpContext http, TenantService svc, BillingService billing,
        ISessionStore sessions, IdentityOptions options, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "validation", detail: "Name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.OwnerSubject))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "validation", detail: "OwnerSubject is required.");
        }

        var now = DateTimeOffset.UtcNow;
        var result = await svc.CreateTenantAsync(
            new CreateTenantCommand(request.Name, request.OwnerSubject, request.OwnerDisplayName, now), ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Results.Problem(statusCode: TenantHttp.MapStatus(result.Error!.Value.Code), title: result.Error.Value.Code, detail: result.Error.Value.Message);
        }

        // New hosted tenants are explicitly provisioned on the configured
        // trial plan so entitlement state is visible from creation. There is
        // no hidden default: without a configured trial plan nothing is
        // provisioned. SingleOwner keeps legacy behavior (no billing rows).
        if (options.IsHosted)
        {
            await billing.EnsureTrialAsync(Id<Tenant>.From(result.Value.Tenant.TenantId), now, ct).ConfigureAwait(false);
        }

        var session = await sessions.IssueAsync(
            Id<Tenant>.From(result.Value.Tenant.TenantId), result.Value.Owner.Subject, now, ct).ConfigureAwait(false);
        TenantHttp.AppendSessionCookie(http, options, session.Token, session.ExpiresAtUtc);
        return Results.Created($"/api/v1/tenants/{result.Value.Tenant.TenantId}", result.Value.Tenant);
    }

    private static async Task<IResult> GetTenantAsync(
        Guid tenantId, HttpContext http, TenantService svc,
        ISessionStore sessions, IdentityOptions options, CancellationToken ct)
    {
        var (_, error) = await RequireTenantMemberAsync(http, tenantId, svc, sessions, options, ct).ConfigureAwait(false);
        if (error is not null) return error;
        var tenant = await svc.GetTenantAsync(Id<Tenant>.From(tenantId), ct).ConfigureAwait(false);
        return tenant is null ? Results.NotFound() : Results.Ok(tenant);
    }

    private static async Task<IResult> ListMembersAsync(
        Guid tenantId, HttpContext http, TenantService svc,
        ISessionStore sessions, IdentityOptions options, CancellationToken ct)
    {
        var (_, error) = await RequireTenantMemberAsync(http, tenantId, svc, sessions, options, ct).ConfigureAwait(false);
        if (error is not null) return error;
        var members = await svc.ListMembersAsync(Id<Tenant>.From(tenantId), ct).ConfigureAwait(false);
        return Results.Ok(members);
    }

    private static async Task<IResult> InviteAsync(
        Guid tenantId, InviteMemberRequest request, HttpContext http, TenantService svc,
        ISessionStore sessions, IdentityOptions options, CancellationToken ct)
    {
        var (principal, error) = await RequireTenantMemberAsync(http, tenantId, svc, sessions, options, ct).ConfigureAwait(false);
        if (error is not null) return error;
        if (request is null || string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "validation", detail: "DisplayName is required.");
        }

        if (!Enum.TryParse<TenantRole>(request.Role, ignoreCase: true, out var role))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "validation", detail: "Role must be Owner, Editor or Viewer.");
        }

        var actor = principal?.Subject ?? request.ActorSubject ?? string.Empty;
        var result = await svc.InviteAsync(
            new InviteMemberCommand(Id<Tenant>.From(tenantId), actor, request.DisplayName, role, DateTimeOffset.UtcNow), ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Results.Problem(statusCode: TenantHttp.MapStatus(result.Error!.Value.Code), title: result.Error.Value.Code, detail: result.Error.Value.Message);
        }

        // The invite token is returned once to the inviting owner; the member
        // list never exposes it.
        return Results.Created(
            $"/api/v1/tenants/{tenantId}/members/{result.Value.Member.MembershipId}",
            new InviteCreatedDto(result.Value.Member, result.Value.InviteToken));
    }

    private static async Task<IResult> AcceptInviteAsync(
        AcceptInviteRequest request, HttpContext http, TenantService svc,
        ISessionStore sessions, IdentityOptions options, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.Subject))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "validation", detail: "Token and Subject are required.");
        }

        var now = DateTimeOffset.UtcNow;
        var result = await svc.AcceptInviteAsync(request.Token, request.Subject, now, ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Results.Problem(statusCode: TenantHttp.MapStatus(result.Error!.Value.Code), title: result.Error.Value.Code, detail: result.Error.Value.Message);
        }

        TenantHttp.AppendSessionCookie(http, options, result.Value.SessionToken, result.Value.SessionExpiresAtUtc);
        return Results.Ok(result.Value.Member);
    }

    private static async Task<IResult> ChangeRoleAsync(
        Guid tenantId, Guid membershipId, ChangeRoleRequest request, HttpContext http, TenantService svc,
        ISessionStore sessions, IdentityOptions options, CancellationToken ct)
    {
        var (principal, error) = await RequireTenantMemberAsync(http, tenantId, svc, sessions, options, ct).ConfigureAwait(false);
        if (error is not null) return error;
        if (request is null || string.IsNullOrWhiteSpace(request.Role)
            || !Enum.TryParse<TenantRole>(request.Role, ignoreCase: true, out var role))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "validation", detail: "Role must be Owner, Editor or Viewer.");
        }

        var actor = principal?.Subject ?? request.ActorSubject ?? string.Empty;
        var result = await svc.ChangeRoleAsync(
            Id<Tenant>.From(tenantId), actor, Id<TenantMembership>.From(membershipId),
            role, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Results.Problem(statusCode: TenantHttp.MapStatus(result.Error!.Value.Code), title: result.Error.Value.Code, detail: result.Error.Value.Message);
        }

        return Results.Ok(result.Value);
    }

    private static async Task<IResult> RevokeAsync(
        Guid tenantId, Guid membershipId, HttpContext http, TenantService svc,
        ISessionStore sessions, IdentityOptions options, CancellationToken ct)
    {
        var (principal, error) = await RequireTenantMemberAsync(http, tenantId, svc, sessions, options, ct).ConfigureAwait(false);
        if (error is not null) return error;
        string? actor = principal?.Subject;
        if (actor is null && http.Request.ContentLength > 0)
        {
            var body = await http.Request.ReadFromJsonAsync<ActorBody>(cancellationToken: ct).ConfigureAwait(false);
            actor = body?.ActorSubject;
        }

        var result = await svc.RevokeAsync(
            Id<Tenant>.From(tenantId), actor ?? string.Empty, Id<TenantMembership>.From(membershipId),
            DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Results.Problem(statusCode: TenantHttp.MapStatus(result.Error!.Value.Code), title: result.Error.Value.Code, detail: result.Error.Value.Message);
        }

        return Results.Ok(result.Value);
    }

    private static async Task<IResult> OidcCallbackAsync(
        OidcCallbackRequest request, HttpContext http, TenantService svc,
        ISessionStore sessions, IdentityOptions options, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Subject) || string.IsNullOrWhiteSpace(request.TenantId))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "validation", detail: "TenantId and Subject are required.");
        }

        if (!Guid.TryParse(request.TenantId, out var tenantId))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "validation", detail: "TenantId must be a GUID.");
        }

        if (!DateTimeOffset.TryParse(request.ExpiresAtUtc, out var expiresAt))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "validation", detail: "ExpiresAtUtc must be an RFC3339 timestamp.");
        }

        var now = DateTimeOffset.UtcNow;
        var result = await svc.OidcSignInAsync(
            new OidcCallbackCommand(
                request.Issuer ?? string.Empty, request.Subject, request.Audience ?? string.Empty,
                expiresAt, request.DisplayName, now),
            Id<Tenant>.From(tenantId), ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Results.Problem(statusCode: TenantHttp.MapStatus(result.Error!.Value.Code), title: result.Error.Value.Code, detail: result.Error.Value.Message);
        }

        TenantHttp.AppendSessionCookie(http, options, result.Value.SessionToken, result.Value.SessionExpiresAtUtc);
        return Results.Ok(result.Value.Member);
    }

    private static async Task<IResult> MigrateAsync(
        Guid tenantId, HttpContext http, TenantMigrationService migration, TenantService svc,
        ISessionStore sessions, IdentityOptions options, CancellationToken ct)
    {
        var (principal, error) = await RequireTenantMemberAsync(http, tenantId, svc, sessions, options, ct).ConfigureAwait(false);
        if (error is not null) return error;
        if (options.IsHosted && (principal is null || !TenantAuthorization.CanManageMembers(principal.Role)))
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "forbidden", detail: "Migration requires the Owner role.");
        }

        var result = await migration.MigrateAsync(Id<Tenant>.From(tenantId), DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Results.Problem(statusCode: TenantHttp.MapStatus(result.Error!.Value.Code), title: result.Error.Value.Code, detail: result.Error.Value.Message);
        }

        return Results.Ok(new MigrationDto(result.Value.Assigned, result.Value.Total, result.Value.TenantId));
    }
}

public sealed record CreateTenantRequest(string Name, string OwnerSubject, string? OwnerDisplayName);

public sealed record InviteMemberRequest(string DisplayName, string Role, string? ActorSubject);

public sealed record InviteCreatedDto(TenantMemberDto Member, string? InviteToken);

public sealed record AcceptInviteRequest(string Token, string Subject);

public sealed record ChangeRoleRequest(string Role, string? ActorSubject);

public sealed record ActorBody(string? ActorSubject);

public sealed record OidcCallbackRequest(
    string TenantId, string? Issuer, string Subject, string? Audience, string ExpiresAtUtc, string? DisplayName);

public sealed record MigrationDto(int Assigned, int Total, Guid TenantId);
