using System.Text.Json;
using System.Text.Json.Serialization;
using Argoscope.Application.Collection;
using Argoscope.Application.Packages;
using Argoscope.Application.Portfolios;
using Argoscope.Domain.Common;
using Argoscope.Domain.Memberships;
using Argoscope.Domain.Packages;
using Argoscope.Domain.Portfolios;
using Argoscope.Domain.Repositories;
using Argoscope.GitHub;
using Argoscope.Packages;
using Microsoft.Extensions.Hosting;

namespace Argoscope.Api;

/// <summary>
/// Seeds the in-memory fake GitHub provider, creates a sample portfolio, adds
/// memberships, and runs a synchronous collection pass. Activated only when
/// the <c>Seed</c> configuration section is present (used by docs screenshot
/// capture and local demos). Synthetic data only — no real GitHub data.
/// </summary>
public sealed class ScreenshotSeedHostedService : IHostedService
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IServiceProvider _services;
    private readonly IConfiguration _configuration;

    public ScreenshotSeedHostedService(
        IHostApplicationLifetime lifetime,
        IServiceProvider services,
        IConfiguration configuration)
    {
        _lifetime = lifetime;
        _services = services;
        _configuration = configuration;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Run after the host is fully started so the listening socket is up before any heavy work.
        _lifetime.ApplicationStarted.Register(() => _ = RunAsync());
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task RunAsync()
    {
        try
        {
            var seed = _configuration.GetSection("Seed").Get<SeedConfig>();
            if (seed is null || seed.Repositories.Count == 0)
            {
                Console.WriteLine("[seed] no seed config; skipping");
                return;
            }
            Console.WriteLine($"[seed] {seed.Repositories.Count} repositories to seed");

            // 1. Seed the GitHub fake provider with synthetic observations.
            var fake = _services.GetService<FakeGitHubRepositoryProvider>();
            if (fake is not null)
            {
                SeedFakeProvider(fake, seed);
                Console.WriteLine("[seed] fake provider seeded");
            }
            else
            {
                Console.WriteLine("[seed] GitHub fake provider not registered; skipping fixture seed");
            }

            // 1b. Seed the package provider fakes with synthetic
            // observations. The fakes are pre-registered in Program.cs
            // when the Seed config is present; the seeder just
            // populates them.
            SeedPackageProviders(_services, seed);
            Console.WriteLine("[seed] package providers seeded");

            // 2. Create the portfolio, add memberships, and run a
            // collection pass per repo.
            using var scope = _services.CreateScope();
            var portfolioService = scope.ServiceProvider.GetRequiredService<PortfolioService>();
            var collectionService = scope.ServiceProvider.GetRequiredService<CollectionService>();
            var packageAssocService = scope.ServiceProvider.GetRequiredService<PackageAssociationService>();
            var packageCollection = scope.ServiceProvider.GetRequiredService<PackageCollectionService>();

            var portfolioResult = await portfolioService.CreateAsync(
                new CreatePortfolioCommand(seed.PortfolioName ?? "Sample portfolio", DateTimeOffset.UtcNow),
                CancellationToken.None).ConfigureAwait(false);
            if (!portfolioResult.IsSuccess)
            {
                return;
            }
            var portfolioId = portfolioResult.Value.Id;

            var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
            foreach (var r in seed.Repositories)
            {
                var add = await portfolioService.AddRepositoryAsync(new AddRepositoryCommand(
                    Id<Portfolio>.From(portfolioId), r.NodeId, r.OwnerLogin, r.Name,
                    Enum.Parse<RepositoryVisibility>(r.Visibility, ignoreCase: true),
                    Enum.Parse<MembershipRole>(r.Role, ignoreCase: true),
                    r.Category, r.Lifecycle, DateTimeOffset.UtcNow), CancellationToken.None).ConfigureAwait(false);
                if (!add.IsSuccess)
                {
                    Console.WriteLine($"[seed] AddRepository failed for {r.OwnerLogin}/{r.Name}: {add.Error?.Code} {add.Error?.Message}");
                    continue;
                }
                var run = await collectionService.RunAsync(new CollectionRequest(
                    Id<Portfolio>.From(portfolioId), r.OwnerLogin, r.Name, today, DateTimeOffset.UtcNow),
                    CancellationToken.None).ConfigureAwait(false);
                Console.WriteLine($"[seed] collection for {r.OwnerLogin}/{r.Name}: snapshots={run.SnapshotsWritten} repoStatus={run.RepositoryStatus} metricsStatus={run.MetricsStatus} engagementStatus={run.EngagementStatus}");

                // Create owner-managed package associations and run an
                // initial collection pass for each.
                foreach (var pkg in r.Packages)
                {
                    var assocResult = await packageAssocService.CreateAsync(
                        new CreatePackageAssociationCommand(
                            Id<Repository>.From(add.Value.RepositoryId),
                            Enum.Parse<PackageProvider>(pkg.Provider, ignoreCase: true),
                            pkg.Coordinate,
                            null, null, DateTimeOffset.UtcNow),
                        CancellationToken.None).ConfigureAwait(false);
                    if (!assocResult.IsSuccess)
                    {
                        Console.WriteLine($"[seed] AddPackage failed for {pkg.Provider}/{pkg.Coordinate}: {assocResult.Error?.Code} {assocResult.Error?.Message}");
                        continue;
                    }
                    var assoc = assocResult.Value;
                    var pUnit = Enum.Parse<PackageUnit>(assoc.DefaultUnit, ignoreCase: true);
                    var pWindow = Enum.Parse<PackageWindow>(assoc.DefaultWindow, ignoreCase: true);
                    var pRun = await packageCollection.RunAsync(new PackageCollectionRequest(
                        Id<Repository>.From(add.Value.RepositoryId),
                        Id<PackageAssociation>.From(assoc.AssociationId),
                        Enum.Parse<PackageProvider>(assoc.Provider, ignoreCase: true),
                        assoc.Coordinate,
                        pUnit, pWindow, DateTimeOffset.UtcNow), CancellationToken.None).ConfigureAwait(false);
                    Console.WriteLine($"[seed] package collection for {pkg.Provider}/{pkg.Coordinate}: written={pRun.ObservationsWritten} preserved={pRun.ObservationsPreserved} meta={pRun.MetadataStatus} page={pRun.PageStatus}");
                }
            }
        }
        catch (Exception ex)
        {
            // Seeder is best-effort; never crash the host.
            Console.WriteLine($"[seed] FAILED: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private static void SeedFakeProvider(FakeGitHubRepositoryProvider fake, SeedConfig seed)
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        foreach (var r in seed.Repositories)
        {
            var created = ParseDate(r.CreatedOnGithub) ?? today.AddYears(-2);
            fake.AddRepository(new FakeGitHubRepositoryProvider.RepositoryFixture(
                r.OwnerLogin, r.Name, r.NodeId,
                Enum.Parse<RepositoryVisibility>(r.Visibility, ignoreCase: true),
                created, r.PrimaryLanguage, LastActivityAtUtc: null));

            // Emit one observation per day for the configured metric sequences, ending today.
            // 60 values → covers the 7d, 30d windows and 30d acceleration baseline.
            foreach (var (metricName, series) in r.Metrics)
            {
                var values = series.Values;
                if (values.Count == 0) continue;
                for (var i = 0; i < values.Count; i++)
                {
                    var date = today.AddDays(-(values.Count - 1 - i));
                    fake.AddMetric(new FakeGitHubRepositoryProvider.MetricFixture(
                        r.OwnerLogin, r.Name, metricName, date, values[i]));
                }
            }
        }
    }

    /// <summary>
    /// Populate each pre-registered <see cref="FakePackageMetricsProvider"/>
    /// with the synthetic observations defined in the seed config. The
    /// fakes are pre-registered as singletons in Program.cs so the
    /// seeder can resolve them from the service provider without
    /// mutating the service collection after host startup.
    /// </summary>
    private static void SeedPackageProviders(IServiceProvider services, SeedConfig seed)
    {
        var providers = new HashSet<PackageProvider>();
        foreach (var r in seed.Repositories)
        {
            foreach (var pkg in r.Packages)
            {
                if (Enum.TryParse<PackageProvider>(pkg.Provider, ignoreCase: true, out var p))
                {
                    providers.Add(p);
                }
            }
        }
        if (providers.Count == 0) return;

        foreach (var provider in providers)
        {
            // The screenshot seeder is the only consumer of the
            // IGetAllFakePackageProviders interface; the API also
            // exposes the individual fakes through DI but listing
            // them all is not currently needed outside seeding.
            var fakes = services.GetServices<IPackageMetricsProvider>()
                .OfType<FakePackageMetricsProvider>()
                .Where(f => f.Provider == provider)
                .ToList();
            foreach (var fake in fakes)
            {
                SeedFakePackageProvider(fake, seed, provider);
            }
        }
    }

    private static void SeedFakePackageProvider(FakePackageMetricsProvider fake, SeedConfig seed, PackageProvider provider)
    {
        var today = DateTimeOffset.UtcNow.Date;
        foreach (var r in seed.Repositories)
        {
            foreach (var pkg in r.Packages)
            {
                if (!Enum.TryParse<PackageProvider>(pkg.Provider, ignoreCase: true, out var p) || p != provider) continue;
                var unit = Enum.Parse<PackageUnit>(pkg.Unit, ignoreCase: true);
                var window = Enum.Parse<PackageWindow>(pkg.Window, ignoreCase: true);
                fake.AddExistence(pkg.Coordinate, exists: true);
                var values = pkg.DailyValues;
                if (values.Count == 0) continue;
                for (var i = 0; i < values.Count; i++)
                {
                    var day = today.AddDays(-(values.Count - 1 - i));
                    var windowEnd = window switch
                    {
                        PackageWindow.Cumulative => today.AddDays(1),
                        PackageWindow.Daily => day.AddDays(1),
                        PackageWindow.Weekly => day.AddDays(7),
                        PackageWindow.Monthly => day.AddDays(30),
                        _ => day.AddDays(1),
                    };
                    fake.AddObservation(new FakePackageMetricsProvider.ObservationFixture(
                        pkg.Coordinate, unit, window, day, windowEnd, values[i], day));
                }
            }
        }
    }

    private static DateOnly? ParseDate(string? s) =>
        DateOnly.TryParse(s, out var d) ? d : null;

    private sealed class SeedConfig
    {
        [JsonPropertyName("PortfolioName")] public string? PortfolioName { get; set; }
        [JsonPropertyName("Repositories")] public List<SeedRepo> Repositories { get; set; } = new();
    }

    private sealed class SeedRepo
    {
        [JsonPropertyName("ownerLogin")] public string OwnerLogin { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("nodeId")] public string NodeId { get; set; } = "";
        [JsonPropertyName("visibility")] public string Visibility { get; set; } = "Public";
        [JsonPropertyName("role")] public string Role { get; set; } = "Owned";
        [JsonPropertyName("category")] public string? Category { get; set; }
        [JsonPropertyName("lifecycle")] public string Lifecycle { get; set; } = "Active";
        [JsonPropertyName("primaryLanguage")] public string? PrimaryLanguage { get; set; }
        [JsonPropertyName("createdOnGithub")] public string? CreatedOnGithub { get; set; }
        [JsonPropertyName("metrics")] public Dictionary<string, SeedMetric> Metrics { get; set; } = new();
        [JsonPropertyName("packages")] public List<SeedPackage> Packages { get; set; } = new();
    }

    private sealed class SeedMetric
    {
        [JsonPropertyName("values")] public List<double> Values { get; set; } = new();
    }

    private sealed class SeedPackage
    {
        [JsonPropertyName("provider")] public string Provider { get; set; } = "";
        [JsonPropertyName("coordinate")] public string Coordinate { get; set; } = "";
        [JsonPropertyName("unit")] public string Unit { get; set; } = "Downloads";
        [JsonPropertyName("window")] public string Window { get; set; } = "Daily";
        [JsonPropertyName("dailyValues")] public List<double> DailyValues { get; set; } = new();
    }
}
