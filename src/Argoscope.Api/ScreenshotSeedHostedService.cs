using System.Text.Json;
using System.Text.Json.Serialization;
using Argoscope.Application.Collection;
using Argoscope.Application.Portfolios;
using Argoscope.Domain.Common;
using Argoscope.Domain.Memberships;
using Argoscope.Domain.Portfolios;
using Argoscope.Domain.Repositories;
using Argoscope.GitHub;
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

            // 1. Seed the fake provider with synthetic observations.
            var fake = _services.GetService<FakeGitHubRepositoryProvider>();
            if (fake is not null)
            {
                SeedFakeProvider(fake, seed);
                Console.WriteLine("[seed] fake provider seeded");
            }
            else
            {
                Console.WriteLine("[seed] fake provider not registered; skipping fixture seed");
            }

            // 2. Create the portfolio, add memberships, and run a collection pass per repo.
            using var scope = _services.CreateScope();
            var portfolioService = scope.ServiceProvider.GetRequiredService<PortfolioService>();
            var collectionService = scope.ServiceProvider.GetRequiredService<CollectionService>();

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
    }

    private sealed class SeedMetric
    {
        [JsonPropertyName("values")] public List<double> Values { get; set; } = new();
    }
}
