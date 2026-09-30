using Argoscope.Api;
using Argoscope.Application.Analytics;
using Argoscope.Application.Collection;
using Argoscope.Application.Portfolios;
using Argoscope.Application.Ranking;
using Argoscope.Domain.Common;
using Argoscope.GitHub;
using Argoscope.Infrastructure.Persistence;
using Argoscope.Infrastructure.Scheduling;
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

// Application services.
builder.Services.AddScoped<IPortfolioRepository, EfPortfolioRepository>();
builder.Services.AddScoped<IWritePortfolioRepository, EfPortfolioRepository>();
builder.Services.AddScoped<IRepositoryStore, EfRepositoryStore>();
builder.Services.AddScoped<IMembershipStore, EfMembershipStore>();
builder.Services.AddScoped<IMetricSnapshotStore, EfMetricSnapshotStore>();
builder.Services.AddScoped<IEngagementStore, EfEngagementStore>();
builder.Services.AddScoped<ICheckpointStore, EfCheckpointStore>();
builder.Services.AddScoped<IScoreConfigurationStore, EfScoreConfigurationStore>();
builder.Services.AddScoped<CollectionService>();
builder.Services.AddScoped<AnalyticsService>();
builder.Services.AddScoped<PortfolioService>();
builder.Services.AddScoped<PriorityScoreService>();

// Daily collection job (only when the real provider is configured).
if (!string.IsNullOrWhiteSpace(ghToken))
{
    builder.Services.AddHostedService<DailyCollectionHostedService>();
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
