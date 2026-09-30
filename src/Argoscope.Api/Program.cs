using Argoscope.Api;
using Argoscope.Application.Analytics;
using Argoscope.Application.Collection;
using Argoscope.Application.Packages;
using Argoscope.Application.Portfolios;
using Argoscope.Application.Ranking;
using Argoscope.Domain.Common;
using Argoscope.Domain.Packages;
using Argoscope.GitHub;
using Argoscope.Infrastructure.Persistence;
using Argoscope.Infrastructure.Scheduling;
using Argoscope.Packages;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.Configure<GitHubProviderOptions>(builder.Configuration.GetSection("GitHub"));
builder.Services.Configure<DailyCollectionOptions>(builder.Configuration.GetSection("Argoscope:Collection"));
builder.Services.Configure<PackageCollectionOptions>(builder.Configuration.GetSection("Argoscope:Packages"));

// Persistence: PostgreSQL when configured, otherwise InMemory for the development shell.
var connectionString = builder.Configuration.GetConnectionString("Argoscope");
if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddDbContext<ArgoscopeDbContext>(opts => opts.UseNpgsql(connectionString));
}
else
{
    builder.Services.AddDbContext<ArgoscopeDbContext>(opts => opts.UseInMemoryDatabase("argoscope-dev"));
}

// GitHub provider: real HTTP if a token is configured, fake otherwise.
var ghToken = builder.Configuration["GitHub:Token"];
if (!string.IsNullOrWhiteSpace(ghToken))
{
    builder.Services.AddGitHubRepositoryProvider(ghToken);
}
else
{
    builder.Services.AddSingleton(new FakeGitHubRepositoryProvider());
    builder.Services.AddSingleton<IGitHubRepositoryProvider>(sp => sp.GetRequiredService<FakeGitHubRepositoryProvider>());
}

// Package providers: stub HTTP adapters by default. The screenshot
// seeder and the integration tests replace the stub for the relevant
// providers with in-memory fakes so the collection path can be
// exercised without network access.
builder.Services.AddStubPackageProviders();
builder.Services.AddSingleton<PackageProviderRegistry>();

// When the Seed config section is present and no real GitHub token is
// configured, pre-register empty FakePackageMetricsProvider instances
// for every supported provider. The screenshot seeder populates them
// with synthetic observations and the collection pipeline resolves
// them through PackageProviderRegistry.
var hasSeedConfigForPackages = builder.Configuration.GetSection("Seed").Exists();
if (hasSeedConfigForPackages && string.IsNullOrWhiteSpace(ghToken))
{
    // The stub providers are registered as factory lambdas, so the
    // usual implementation-instance filter does not match them; drop
    // every IPackageMetricsProvider registration and re-register one
    // fake per provider.
    var existing = builder.Services
        .Where(d => d.ServiceType == typeof(IPackageMetricsProvider))
        .ToList();
    foreach (var d in existing) builder.Services.Remove(d);

    foreach (PackageProvider provider in Enum.GetValues<PackageProvider>())
    {
        var fake = new FakePackageMetricsProvider(provider,
            providerVersion: $"fake-{provider.ToString().ToLowerInvariant()}-1");
        builder.Services.AddSingleton(fake);
        builder.Services.AddSingleton<IPackageMetricsProvider>(fake);
    }
}

// Application services.
builder.Services.AddScoped<IPortfolioRepository, EfPortfolioRepository>();
builder.Services.AddScoped<IWritePortfolioRepository, EfPortfolioRepository>();
builder.Services.AddScoped<IRepositoryStore, EfRepositoryStore>();
builder.Services.AddScoped<IMembershipStore, EfMembershipStore>();
builder.Services.AddScoped<IMetricSnapshotStore, EfMetricSnapshotStore>();
builder.Services.AddScoped<IEngagementStore, EfEngagementStore>();
builder.Services.AddScoped<ICheckpointStore, EfCheckpointStore>();
builder.Services.AddScoped<IScoreConfigurationStore, EfScoreConfigurationStore>();
builder.Services.AddScoped<IPackageAssociationStore, EfPackageAssociationStore>();
builder.Services.AddScoped<IPackageObservationStore, EfPackageObservationStore>();
builder.Services.AddScoped<CollectionService>();
builder.Services.AddScoped<AnalyticsService>();
builder.Services.AddScoped<PortfolioService>();
builder.Services.AddScoped<PriorityScoreService>();
builder.Services.AddScoped<PackageAssociationService>();
builder.Services.AddScoped<PackageAdoptionService>();
builder.Services.AddScoped<PackageCollectionService>();

// Daily collection jobs (only when the real provider is configured).
if (!string.IsNullOrWhiteSpace(ghToken))
{
    builder.Services.AddHostedService<DailyCollectionHostedService>();
    builder.Services.AddHostedService<PackageCollectionHostedService>();
}

// Optional docs/screenshot seed: active only when the Seed config section exists (no real token, synthetic data only).
var hasSeedConfig = builder.Configuration.GetSection("Seed").Exists();
if (hasSeedConfig && string.IsNullOrWhiteSpace(ghToken))
{
    builder.Services.AddHostedService<ScreenshotSeedHostedService>();
}

builder.Services.ConfigureHttpJsonOptions(opts => ArgoscopeJson.Apply(opts.SerializerOptions));
builder.Services.AddProblemDetails();
builder.Services.AddCors(opts => opts.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ArgoscopeDbContext>();
    if (db.Database.IsInMemory())
    {
        db.Database.EnsureCreated();
    }
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
// SPA fallback: any non-API GET that didn't match a static file serves index.html so the React router can take over.
app.MapFallback(async ctx =>
{
    if (HttpMethods.IsGet(ctx.Request.Method) && !ctx.Request.Path.StartsWithSegments("/api"))
    {
        var indexPath = Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html");
        if (File.Exists(indexPath))
        {
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.SendFileAsync(indexPath);
            return;
        }
    }
    ctx.Response.StatusCode = StatusCodes.Status404NotFound;
});
app.MapArgoscopeApi();
app.MapGet("/api/v1/health", () => Results.Ok(new { status = "ok", time = DateTimeOffset.UtcNow }));

app.Run();

// Expose Program for WebApplicationFactory in integration tests.
public partial class Program { }
