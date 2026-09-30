using Argoscope.Domain.Common;
using Argoscope.Domain.Identity;

namespace Argoscope.Application.Identity;

/// <summary>
/// Assigns legacy single-owner rows to an explicit tenant before hosted
/// rollout. Validation runs before any write, so a failure is atomic (no
/// partial assignment); hosted access stays disabled until verification
/// reports zero unscoped rows.
/// </summary>
public sealed class TenantMigrationService
{
    private readonly IPortfolioTenantStore _portfolios;
    private readonly ITenantStore _tenants;

    public TenantMigrationService(IPortfolioTenantStore portfolios, ITenantStore tenants)
    {
        _portfolios = portfolios;
        _tenants = tenants;
    }

    public sealed record MigrationResult(int Assigned, int Total, Guid TenantId);

    public async Task<Result<MigrationResult>> MigrateAsync(
        Id<Tenant> tenantId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var tenant = await _tenants.FindAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (tenant is null) return Error.NotFound("Tenant not found.");

        var portfolios = await _portfolios.ListAllPortfoliosAsync(cancellationToken).ConfigureAwait(false);

        // Pre-validate everything before writing anything: atomic failure.
        foreach (var portfolio in portfolios)
        {
            if (string.IsNullOrWhiteSpace(portfolio.Name))
            {
                return Error.Conflict("Migration failed: a legacy row cannot be assigned; hosted access remains disabled.");
            }
        }

        var assigned = 0;
        var original = portfolios.ToDictionary(p => p.Id, p => p.TenantId);
        foreach (var portfolio in portfolios)
        {
            if (portfolio.TenantId is null)
            {
                portfolio.AssignTenant(tenantId, now);
                assigned++;
            }
        }

        try
        {
            await _portfolios.SaveAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Revert in-memory assignments so a persistence failure leaves
            // rows unscoped (atomic outcome); hosted access stays disabled.
            foreach (var portfolio in portfolios)
            {
                if (original[portfolio.Id] is null && portfolio.TenantId == tenantId)
                {
                    portfolio.ClearTenantForRollback(now);
                }
            }

            throw;
        }

        var remaining = await CountUnscopedAsync(cancellationToken).ConfigureAwait(false);
        if (remaining > 0)
        {
            return Error.Conflict("Migration verification failed: unscoped rows remain; hosted access remains disabled.");
        }

        return new MigrationResult(assigned, portfolios.Count, tenantId.Value);
    }

    public async Task<int> CountUnscopedAsync(CancellationToken cancellationToken)
    {
        var portfolios = await _portfolios.ListAllPortfoliosAsync(cancellationToken).ConfigureAwait(false);
        return portfolios.Count(p => p.TenantId is null);
    }
}

/// <summary>
/// Explicit tenant scope carried by asynchronous jobs. Every hosted job run
/// resolves the resource's tenant before executing and never falls back to a
/// global query when the context is missing.
/// </summary>
public static class TenantJobScope
{
    /// <summary>Cache key namespace: tenant id is always part of the key.</summary>
    public static string CacheKey(Id<Tenant>? tenantId, string key) =>
        $"tenant:{(tenantId is null ? "unassigned" : tenantId.Value.ToString())}:{key}";

    /// <summary>Log scope properties so tenant attribution is observable.</summary>
    public static IReadOnlyDictionary<string, string?> LogScope(Id<Tenant>? tenantId, Guid resourceId) =>
        new Dictionary<string, string?>
        {
            ["tenantId"] = tenantId?.Value.ToString(),
            ["resourceId"] = resourceId.ToString(),
        };
}
