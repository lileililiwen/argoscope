using Argoscope.Domain.Packages;

namespace Argoscope.Packages;

/// <summary>
/// Resolves the registered <see cref="IPackageMetricsProvider"/> for a
/// given <see cref="PackageProvider"/>. Exactly one implementation is
/// expected per provider; if none is registered, the resolver returns
/// null and the collection service treats the provider as
/// not-implemented. This indirection keeps the registration list
/// additive and avoids keyed-service lookups in the application layer.
/// </summary>
public sealed class PackageProviderRegistry
{
    private readonly Dictionary<PackageProvider, IPackageMetricsProvider> _byProvider;

    public PackageProviderRegistry(IEnumerable<IPackageMetricsProvider> providers)
    {
        _byProvider = providers.ToDictionary(p => p.Provider);
    }

    public IPackageMetricsProvider? Resolve(PackageProvider provider) =>
        _byProvider.TryGetValue(provider, out var p) ? p : null;

    public IReadOnlyDictionary<PackageProvider, IPackageMetricsProvider> All => _byProvider;
}
