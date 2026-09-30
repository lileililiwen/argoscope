using Argoscope.Domain.Packages;
using Argoscope.Domain.Snapshots;
using Microsoft.Extensions.DependencyInjection;

namespace Argoscope.Packages;

/// <summary>
/// Stub HTTP adapter for a single package provider. The MVP only uses
/// the in-memory fake for tests and the screenshot seed; the real HTTP
/// adapters are intentionally not implemented and return
/// <see cref="ProviderResultStatus.Unavailable"/> with a
/// <c>provider-not-implemented</c> diagnostic, matching the GitHub
/// real-provider behavior. The infrastructure layer is the appropriate
/// place to add concrete registry HTTP clients later — this file
/// establishes the contract and the failure surface.
/// </summary>
public sealed class StubHttpPackageMetricsProvider : IPackageMetricsProvider
{
    public string ProviderVersion { get; }

    public PackageProvider Provider { get; }

    public StubHttpPackageMetricsProvider(PackageProvider provider, string providerVersion)
    {
        Provider = provider;
        ProviderVersion = providerVersion;
    }

    public Task<PackageMetadataResult> ProbeAsync(string coordinate, CancellationToken cancellationToken) =>
        Task.FromResult(PackageMetadataResult.Failure(ProviderResultStatus.Unavailable, "provider-not-implemented"));

    public Task<PackageMetricPage> GetObservationsAsync(
        string coordinate,
        PackageUnit unit,
        PackageWindow window,
        DateTimeOffset sinceUtc,
        string? cursor,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new PackageMetricPage(
            Array.Empty<PackageMetricObservation>(),
            null,
            ProviderResultStatus.Unavailable,
            null,
            "provider-not-implemented"));
    }
}

/// <summary>
/// DI extensions for the package provider family. Tests register the
/// fake per provider; the API registers the stub adapter by default and
/// lets tests replace the registration with the fake via
/// <see cref="ReplaceWithFake"/>.
/// </summary>
public static class PackageProviderServiceCollectionExtensions
{
    public const string DefaultProviderVersion = "stub-1";

    /// <summary>
    /// Register the stub HTTP adapter for every supported provider. The
    /// real HTTP clients will be added here once the dotnet-platform-libs
    /// registry package is published; until then the stub is the only
    /// production-side implementation.
    /// </summary>
    public static IServiceCollection AddStubPackageProviders(this IServiceCollection services, string? providerVersion = null)
    {
        var version = string.IsNullOrWhiteSpace(providerVersion) ? DefaultProviderVersion : providerVersion;
        services.AddSingleton<IPackageMetricsProvider>(_ => new StubHttpPackageMetricsProvider(PackageProvider.DockerHub, version));
        services.AddSingleton<IPackageMetricsProvider>(_ => new StubHttpPackageMetricsProvider(PackageProvider.Npm, version));
        services.AddSingleton<IPackageMetricsProvider>(_ => new StubHttpPackageMetricsProvider(PackageProvider.NuGet, version));
        services.AddSingleton<IPackageMetricsProvider>(_ => new StubHttpPackageMetricsProvider(PackageProvider.PyPI, version));
        services.AddSingleton<IPackageMetricsProvider>(_ => new StubHttpPackageMetricsProvider(PackageProvider.CratesIo, version));
        return services;
    }

    /// <summary>
    /// Register a single fake provider instance. Used by the screenshot
    /// seeder and by integration tests; replaces the stub for the given
    /// provider.
    /// </summary>
    public static IServiceCollection AddFakePackageProvider(this IServiceCollection services, FakePackageMetricsProvider fake)
    {
        // Replace any existing registration for this provider type.
        var existing = services.Where(d => d.ServiceType == typeof(IPackageMetricsProvider)
            && d.ImplementationInstance is StubHttpPackageMetricsProvider stub
            && stub.Provider == fake.Provider).ToList();
        foreach (var d in existing) services.Remove(d);
        services.AddSingleton(fake);
        return services;
    }
}
