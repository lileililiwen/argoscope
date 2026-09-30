using System.Reflection;

namespace Argoscope.Application.Operations;

/// <summary>Immutable release identity. The revision is resolved from the
/// ARGOSCOPE_RELEASE_REVISION environment (CI-tagged container label) and
/// falls back to the assembly informational version; it is recorded on
/// readiness, rehearsal and incident evidence.</summary>
public sealed record ReleaseInfo(string Revision, string Artifact, DateTimeOffset BuiltAtUtc);

public static class ReleaseInfoProvider
{
    public static ReleaseInfo Current(DateTimeOffset? now = null)
    {
        var revision = Environment.GetEnvironmentVariable("ARGOSCOPE_RELEASE_REVISION");
        if (string.IsNullOrWhiteSpace(revision))
        {
            revision = Assembly
                .GetEntryAssembly()
                ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "dev-local";
        }

        revision = revision.Trim();
        var artifact = $"argoscope:{revision}";
        return new ReleaseInfo(revision, artifact, now ?? DateTimeOffset.UtcNow);
    }
}
