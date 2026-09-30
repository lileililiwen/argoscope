namespace Argoscope.Application.Identity;

/// <summary>Deployment profile for identity. SingleOwner preserves the legacy
/// open local behavior (local owner maps to a tenant without external OIDC);
/// Hosted enforces tenant principals, roles and migration gating.</summary>
public sealed class IdentityOptions
{
    public const string SectionName = "Identity";

    /// <summary>SingleOwner or Hosted.</summary>
    public string Mode { get; set; } = "SingleOwner";

    public bool IsHosted => string.Equals(Mode, "Hosted", StringComparison.OrdinalIgnoreCase);

    /// <summary>Expected OIDC issuer (deployment config; empty disables OIDC callback).</summary>
    public string OidcIssuer { get; set; } = string.Empty;

    /// <summary>Expected OIDC audience/client id (deployment config).</summary>
    public string OidcAudience { get; set; } = string.Empty;

    /// <summary>Simulated provider outage flag. When true, new OIDC logins fail
    /// closed while previously issued bounded sessions continue until expiry.</summary>
    public bool OidcProviderUnavailable { get; set; }

    /// <summary>Session lifetime in hours. Default 8.</summary>
    public int SessionHours { get; set; } = 8;

    /// <summary>Header carrying the tenant id for non-browser API clients.</summary>
    public string TenantHeader { get; set; } = "X-Argoscope-Tenant";

    /// <summary>Header carrying the account subject for non-browser API clients.</summary>
    public string UserHeader { get; set; } = "X-Argoscope-User";

    /// <summary>CSRF header required on cookie-authenticated mutations.</summary>
    public string CsrfHeader { get; set; } = "X-Argoscope-Csrf";

    /// <summary>Session cookie name.</summary>
    public string SessionCookie { get; set; } = "argoscope_session";
}
