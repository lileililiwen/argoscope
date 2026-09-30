using Argoscope.Api;
using Argoscope.Domain.Common;
using Argoscope.Domain.Memberships;
using Argoscope.Domain.Packages;
using Argoscope.Domain.Portfolios;
using Argoscope.Domain.Repositories;
using Argoscope.GitHub;
using Argoscope.Infrastructure.Persistence;
using Argoscope.Packages;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Argoscope.IntegrationTests;

public sealed class ApiFactory : ICollectionFixture<ApiFactory>
{
    public WebApplicationFactory<Program> Factory { get; }

    public ApiFactory()
    {
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.ConfigureServices(services =>
            {
                // Replace the real provider with the fake and the real DbContext with the InMemory one.
                var fakeGitHub = new FakeGitHubRepositoryProvider();
                services.RemoveAll<IGitHubRepositoryProvider>();
                services.AddSingleton(fakeGitHub);
                services.AddSingleton<IGitHubRepositoryProvider>(sp => sp.GetRequiredService<FakeGitHubRepositoryProvider>());

                // Replace the stub package providers with the in-memory
                // fakes so the package collection and adoption endpoints
                // can be exercised deterministically.
                services.RemoveAll<IPackageMetricsProvider>();
                foreach (PackageProvider provider in Enum.GetValues<PackageProvider>())
                {
                    var fake = new FakePackageMetricsProvider(provider,
                        providerVersion: $"fake-{provider.ToString().ToLowerInvariant()}-1");
                    services.AddSingleton(fake);
                    services.AddSingleton<IPackageMetricsProvider>(fake);
                }

                services.RemoveAll<DbContextOptions<ArgoscopeDbContext>>();
                services.AddDbContext<ArgoscopeDbContext>(opts => opts.UseInMemoryDatabase("argoscope-test-shared"));
            });
        });
        // Trigger schema creation.
        using var scope = Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ArgoscopeDbContext>().Database.EnsureCreated();
    }
}

[CollectionDefinition("ApiFactory")]
public sealed class ApiFactoryCollection : ICollectionFixture<ApiFactory>
{
}

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection RemoveAll<T>(this IServiceCollection services)
    {
        var descriptors = services.Where(d => d.ServiceType == typeof(T)).ToList();
        foreach (var d in descriptors) services.Remove(d);
        return services;
    }
}
