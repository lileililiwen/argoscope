using Argoscope.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Argoscope.GitHub;

/// <summary>DI extensions for the GitHub provider.</summary>
public static class GitHubServiceCollectionExtensions
{
    /// <summary>
    /// Registers the real REST/GraphQL <see cref="GitHubRepositoryProvider"/>
    /// bound to a named <see cref="HttpClient"/>. Tests call
    /// <c>AddFakeGitHubRepositoryProvider</c> instead.
    /// </summary>
    public static IServiceCollection AddGitHubRepositoryProvider(this IServiceCollection services, string? bearerToken = null)
    {
        services.AddOptions<GitHubProviderOptions>();
        services.AddHttpClient<IGitHubRepositoryProvider, GitHubRepositoryProvider>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<GitHubProviderOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
            if (!string.IsNullOrEmpty(bearerToken))
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);
            }
        });
        return services;
    }

    /// <summary>
    /// Registers a deterministic in-memory provider for tests. The caller
    /// supplies the <see cref="FakeGitHubRepositoryProvider"/>; the same
    /// instance can be retrieved from DI for fixture setup.
    /// </summary>
    public static IServiceCollection AddFakeGitHubRepositoryProvider(this IServiceCollection services, FakeGitHubRepositoryProvider fake)
    {
        services.AddSingleton(fake);
        services.AddSingleton<IGitHubRepositoryProvider>(sp => sp.GetRequiredService<FakeGitHubRepositoryProvider>());
        return services;
    }
}
