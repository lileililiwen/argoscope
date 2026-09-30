using Argoscope.Application.Billing;
using Argoscope.Application.Identity;
using Argoscope.Domain.Common;
using Argoscope.Domain.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Argoscope.Api;

/// <summary>
/// Billing endpoints. The webhook is authenticated by provider HMAC signature
/// (no tenant principal); account endpoints enforce the same tenant-member
/// rules as identity (cross-tenant reads are 404, management is owner-only).
/// Responses expose plan, status, period end, cancellation flag and
/// entitlements only — never payment method details or the webhook secret.
/// </summary>
public static class BillingApiModule
{
    public static IEndpointRouteBuilder MapBillingApi(this IEndpointRouteBuilder builder)
    {
        var v1 = builder.MapGroup("/api/v1").WithGroupName("argoscope-billing");

        v1.MapPost("/billing/webhook", (HttpContext http, BillingService svc, IOptions<BillingOptions> opts, CancellationToken ct) => WebhookAsync(http, svc, opts.Value, ct));
        v1.MapGet("/tenants/{tenantId:guid}/billing", (Guid tenantId, HttpContext http, TenantService tenants, BillingService svc, ISessionStore sessions, IOptions<IdentityOptions> opts, CancellationToken ct) => GetBillingAsync(tenantId, http, tenants, svc, sessions, opts.Value, ct));
        v1.MapPost("/tenants/{tenantId:guid}/billing/checkout", (Guid tenantId, CheckoutRequest request, HttpContext http, TenantService tenants, BillingService svc, ISessionStore sessions, IOptions<IdentityOptions> opts, CancellationToken ct) => CheckoutAsync(tenantId, request, http, tenants, svc, sessions, opts.Value, ct));
        v1.MapPost("/tenants/{tenantId:guid}/billing/cancel", (Guid tenantId, HttpContext http, TenantService tenants, BillingService svc, ISessionStore sessions, IOptions<IdentityOptions> opts, CancellationToken ct) => CancelAsync(tenantId, http, tenants, svc, sessions, opts.Value, ct));
        v1.MapGet("/tenants/{tenantId:guid}/billing/events", (Guid tenantId, HttpContext http, TenantService tenants, BillingService svc, ISessionStore sessions, IOptions<IdentityOptions> opts, CancellationToken ct) => ListEventsAsync(tenantId, http, tenants, svc, sessions, opts.Value, ct));
        v1.MapGet("/tenants/{tenantId:guid}/billing/reconcile", (Guid tenantId, HttpContext http, TenantService tenants, BillingService svc, ISessionStore sessions, IOptions<IdentityOptions> opts, CancellationToken ct) => ReconcileAsync(tenantId, http, tenants, svc, sessions, opts.Value, ct));

        return builder;
    }

    private static int MapStatus(string code) => code switch
    {
        "not_found" => StatusCodes.Status404NotFound,
        "conflict" => StatusCodes.Status409Conflict,
        "unauthorized" => StatusCodes.Status401Unauthorized,
        "forbidden" => StatusCodes.Status403Forbidden,
        "payment_required" => StatusCodes.Status402PaymentRequired,
        "unavailable" => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status400BadRequest,
    };

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

        // Cookie-authenticated mutations require the CSRF header, mirroring
        // the tenant authorization filter.
        if (viaCookie
            && !HttpMethods.IsGet(http.Request.Method)
            && !HttpMethods.IsHead(http.Request.Method)
            && !HttpMethods.IsOptions(http.Request.Method)
            && !string.Equals(http.Request.Headers[options.CsrfHeader].FirstOrDefault(), "1", StringComparison.Ordinal))
        {
            return (null, Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "forbidden", detail: "CSRF header is required."));
        }

        TenantHttp.SetPrincipal(http, principal);
        return (principal, null);
    }

    private static async Task<IResult> WebhookAsync(
        HttpContext http, BillingService svc, BillingOptions options, CancellationToken ct)
    {
        http.Request.EnableBuffering();
        string rawBody;
        using (var reader = new StreamReader(http.Request.Body, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            rawBody = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        }

        http.Request.Body.Position = 0;
        var signature = http.Request.Headers[options.SignatureHeader].FirstOrDefault();
        var result = await svc.ProcessWebhookAsync(rawBody, signature, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Results.Problem(
                statusCode: MapStatus(result.Error!.Value.Code),
                title: result.Error.Value.Code,
                detail: result.Error.Value.Message);
        }

        return Results.Ok(result.Value);
    }

    private static async Task<IResult> GetBillingAsync(
        Guid tenantId, HttpContext http, TenantService tenants, BillingService svc,
        ISessionStore sessions, IdentityOptions options, CancellationToken ct)
    {
        var (_, error) = await RequireTenantMemberAsync(http, tenantId, tenants, sessions, options, ct).ConfigureAwait(false);
        if (error is not null) return error;

        var status = await svc.GetStatusAsync(Id<Tenant>.From(tenantId), DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        return status is null
            ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "not_found", detail: "No subscription is configured for this tenant.")
            : Results.Ok(status);
    }

    private static async Task<IResult> CheckoutAsync(
        Guid tenantId, CheckoutRequest request, HttpContext http, TenantService tenants, BillingService svc,
        ISessionStore sessions, IdentityOptions options, CancellationToken ct)
    {
        var (principal, error) = await RequireTenantMemberAsync(http, tenantId, tenants, sessions, options, ct).ConfigureAwait(false);
        if (error is not null) return error;

        var role = principal?.Role ?? TenantRole.Owner;
        var actor = principal?.Subject ?? string.Empty;
        var result = await svc.StartCheckoutAsync(
            Id<Tenant>.From(tenantId), actor, role, request?.PlanId, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Results.Problem(statusCode: MapStatus(result.Error!.Value.Code), title: result.Error.Value.Code, detail: result.Error.Value.Message);
        }

        return Results.Ok(result.Value);
    }

    private static async Task<IResult> CancelAsync(
        Guid tenantId, HttpContext http, TenantService tenants, BillingService svc,
        ISessionStore sessions, IdentityOptions options, CancellationToken ct)
    {
        var (principal, error) = await RequireTenantMemberAsync(http, tenantId, tenants, sessions, options, ct).ConfigureAwait(false);
        if (error is not null) return error;

        var result = await svc.RequestCancellationAsync(
            Id<Tenant>.From(tenantId), principal?.Role ?? TenantRole.Owner,
            DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Results.Problem(statusCode: MapStatus(result.Error!.Value.Code), title: result.Error.Value.Code, detail: result.Error.Value.Message);
        }

        return Results.Ok(result.Value);
    }

    private static async Task<IResult> ListEventsAsync(
        Guid tenantId, HttpContext http, TenantService tenants, BillingService svc,
        ISessionStore sessions, IdentityOptions options, CancellationToken ct)
    {
        var (principal, error) = await RequireTenantMemberAsync(http, tenantId, tenants, sessions, options, ct).ConfigureAwait(false);
        if (error is not null) return error;
        if (options.IsHosted && (principal is null || !TenantAuthorization.CanManageMembers(principal.Role)))
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "forbidden", detail: "Billing audit requires the Owner role.");
        }

        var events = await svc.ListEventsAsync(Id<Tenant>.From(tenantId), ct).ConfigureAwait(false);
        return Results.Ok(events);
    }

    private static async Task<IResult> ReconcileAsync(
        Guid tenantId, HttpContext http, TenantService tenants, BillingService svc,
        ISessionStore sessions, IdentityOptions options, CancellationToken ct)
    {
        var (principal, error) = await RequireTenantMemberAsync(http, tenantId, tenants, sessions, options, ct).ConfigureAwait(false);
        if (error is not null) return error;
        if (options.IsHosted && (principal is null || !TenantAuthorization.CanManageMembers(principal.Role)))
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "forbidden", detail: "Reconciliation requires the Owner role.");
        }

        var report = await svc.ReconcileAsync(DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        return Results.Ok(report);
    }
}

public sealed record CheckoutRequest(string? PlanId);
