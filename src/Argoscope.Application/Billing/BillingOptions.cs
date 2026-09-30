namespace Argoscope.Application.Billing;

/// <summary>One purchasable plan in the billing catalog. Plans are deployment
/// configuration (Billing:Plans); there is no hidden default plan — a tenant
/// without a subscription row is reported as unconfigured, and new hosted
/// tenants are explicitly provisioned on the configured trial plan.</summary>
public sealed class PlanDefinition
{
    public string PlanId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int MaxPortfolios { get; set; } = 5;

    public bool IncludesAdvancedAnalytics { get; set; } = true;

    public bool IncludesAlerts { get; set; } = true;
}

/// <summary>
/// Billing deployment configuration. Provider selection blocker (B3)
/// resolution: Argoscope integrates exactly one external payment provider
/// through a provider-neutral HMAC-SHA256 webhook contract (contract version
/// "billing-webhook-v1", Stripe-compatible "t,v1" signature scheme with
/// ±5-minute timestamp tolerance). The concrete provider, its SDK/package and
/// the supported regions are a deployment decision recorded here; only the
/// generic adapter ships in this package so no provider SDK is referenced.
/// Raw card data is never handled or stored. The webhook secret lives in
/// secret storage / environment ("Billing:WebhookSecret") and never in the
/// repository, logs or API responses.
/// </summary>
public sealed class BillingOptions
{
    public const string SectionName = "Billing";

    /// <summary>Webhook contract version. Fixed at "billing-webhook-v1".</summary>
    public string ContractVersion { get; set; } = "billing-webhook-v1";

    /// <summary>Configured payment provider name (deployment decision).</summary>
    public string ProviderName { get; set; } = "generic-hmac";

    /// <summary>HMAC webhook secret from secret storage only. Empty fails
    /// webhooks closed (503) rather than accepting unsigned deliveries.</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>Header carrying the "t={unix},v1={hex}" signature.</summary>
    public string SignatureHeader { get; set; } = "X-Billing-Signature";

    /// <summary>Webhook timestamp tolerance; default ±5 minutes.</summary>
    public int TimestampToleranceMinutes { get; set; } = 5;

    /// <summary>Past-due grace window before the account goes read-only.</summary>
    public int PastDueGraceDays { get; set; } = 7;

    /// <summary>Plan id used for explicit trial provisioning of new hosted
    /// tenants. Must name a plan in <see cref="Plans"/>.</summary>
    public string TrialPlanId { get; set; } = "trial";

    /// <summary>Trial length in days for newly provisioned subscriptions.</summary>
    public int TrialDays { get; set; } = 14;

    /// <summary>Stable upgrade URL surfaced with entitlement denials.</summary>
    public string UpgradeUrl { get; set; } = "/tenants/billing/upgrade";

    /// <summary>Provider-hosted checkout base URL (deployment config). Checkout
    /// links are built from provider references only.</summary>
    public string CheckoutBaseUrl { get; set; } = "https://billing.example/checkout";

    public List<PlanDefinition> Plans { get; set; } = new()
    {
        new PlanDefinition { PlanId = "trial", Name = "Trial", MaxPortfolios = 3, IncludesAdvancedAnalytics = true, IncludesAlerts = true },
        new PlanDefinition { PlanId = "starter", Name = "Starter", MaxPortfolios = 5, IncludesAdvancedAnalytics = true, IncludesAlerts = true },
        new PlanDefinition { PlanId = "pro", Name = "Pro", MaxPortfolios = 50, IncludesAdvancedAnalytics = true, IncludesAlerts = true },
    };
}
