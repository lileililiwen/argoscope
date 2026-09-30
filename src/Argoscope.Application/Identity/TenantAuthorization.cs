using Argoscope.Domain.Common;
using Argoscope.Domain.Identity;
using Microsoft.Extensions.Options;

namespace Argoscope.Application.Identity;

/// <summary>
/// Least-privilege checks shared by the API filter and handlers. Viewer is
/// read-only; Editor mutates portfolio data; Owner additionally manages
/// membership. Cross-tenant access is denied as not-found (no disclosure).
/// </summary>
public static class TenantAuthorization
{
    public static bool CanMutate(TenantRole role) => role is TenantRole.Owner or TenantRole.Editor;

    public static bool CanManageMembers(TenantRole role) => role == TenantRole.Owner;

    public static bool Satisfies(TenantRole actual, TenantRole minimum) => actual >= minimum;

    public static bool IsSameTenant(Id<Tenant>? resourceTenantId, Id<Tenant> principalTenantId) =>
        resourceTenantId is not null && resourceTenantId == principalTenantId;
}

/// <summary>
/// Validates OIDC callback claims against deployment config. Signature
/// validation is the deployment OIDC middleware's responsibility; this
/// validator enforces issuer/audience/expiry server-side and fails closed on
/// provider outage or misconfiguration. No network calls.
/// </summary>
public sealed class OidcTokenValidator
{
    private readonly IdentityOptions _options;

    public OidcTokenValidator(IOptions<IdentityOptions> options) => _options = options.Value;

    public Result Validate(OidcCallbackCommand command)
    {
        if (_options.OidcProviderUnavailable)
        {
            return Error.Unavailable("Identity provider is unavailable; new logins fail closed.");
        }

        if (string.IsNullOrWhiteSpace(_options.OidcIssuer)
            || !string.Equals(command.Issuer, _options.OidcIssuer, StringComparison.Ordinal))
        {
            return Error.Unauthorized("OIDC issuer is not trusted.");
        }

        if (string.IsNullOrWhiteSpace(_options.OidcAudience)
            || !string.Equals(command.Audience, _options.OidcAudience, StringComparison.Ordinal))
        {
            return Error.Unauthorized("OIDC audience is not trusted.");
        }

        if (string.IsNullOrWhiteSpace(command.Subject))
        {
            return Error.Validation("OIDC subject is required.");
        }

        if (command.ExpiresAtUtc <= command.Now)
        {
            return Error.Unauthorized("OIDC token has expired.");
        }

        return Result.Success();
    }
}
