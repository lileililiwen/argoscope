using System.Text.RegularExpressions;

namespace Argoscope.Domain.Packages;

/// <summary>
/// Per-provider coordinate validator. Each provider has a documented
/// naming convention; the validator enforces the conventional shape and
/// rejects empty, malformed, or duplicate-coordinator inputs. NuGet
/// package ids are case-insensitive on the wire but the validator
/// normalizes the storage form to lowercase so that the (provider,
/// coordinate) uniqueness key is case-stable.
/// </summary>
public static class PackageCoordinateValidator
{
    // Docker Hub: namespace/name, lowercase, [a-z0-9_.-], cannot start or end with separator.
    // Library namespace (e.g. library/redis) is allowed and the canonical form.
    private static readonly Regex DockerHubPattern = new(
        @"^(?:[a-z0-9]+(?:[._-][a-z0-9]+)*)/[a-z0-9]+(?:[._-][a-z0-9]+)*$",
        RegexOptions.Compiled);

    // npm: scoped (@scope/name) or unscoped (name); lowercase; must start
    // and end with [a-z0-9]; the body may contain . _ - between alphanumeric
    // characters.
    private static readonly Regex NpmPattern = new(
        @"^(?:@[a-z0-9](?:[a-z0-9._-]*[a-z0-9])?/)?[a-z0-9](?:[a-z0-9._-]*[a-z0-9])?$",
        RegexOptions.Compiled);

    // NuGet: case-insensitive; letters/digits/./-; first/last char must be alphanumeric.
    private static readonly Regex NuGetPattern = new(
        @"^[A-Za-z0-9]+(?:[.\-][A-Za-z0-9]+)*$",
        RegexOptions.Compiled);

    // PyPI: lowercase; letters/digits/./-/_; first/last char must be alphanumeric.
    private static readonly Regex PyPIPattern = new(
        @"^[a-z0-9]+(?:[._-][a-z0-9]+)*$",
        RegexOptions.Compiled);

    // crates.io: letters/digits/_/-; first char must be alphanumeric; 1-64 chars.
    private static readonly Regex CratesIoPattern = new(
        @"^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$",
        RegexOptions.Compiled);

    /// <summary>True if the coordinate matches the provider's documented shape.</summary>
    public static bool IsValid(PackageProvider provider, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var trimmed = raw.Trim();
        return provider switch
        {
            PackageProvider.DockerHub => DockerHubPattern.IsMatch(trimmed),
            PackageProvider.Npm => NpmPattern.IsMatch(trimmed),
            PackageProvider.NuGet => NuGetPattern.IsMatch(trimmed),
            PackageProvider.PyPI => PyPIPattern.IsMatch(trimmed),
            PackageProvider.CratesIo => CratesIoPattern.IsMatch(trimmed),
            _ => false,
        };
    }

    /// <summary>
    /// Normalize the coordinate for stable storage and uniqueness. Trims
    /// surrounding whitespace; lowercases for registries that are
    /// case-insensitive (NuGet) or case-sensitive-on-input-only (PyPI, npm,
    /// Docker Hub, crates.io). Crates.io preserves the registered casing
    /// per the registry's "CamelCase allowed" rule.
    /// </summary>
    public static string Normalize(PackageProvider provider, string raw)
    {
        var trimmed = raw.Trim();
        return provider switch
        {
            PackageProvider.NuGet => trimmed.ToLowerInvariant(),
            PackageProvider.PyPI => trimmed.ToLowerInvariant(),
            PackageProvider.Npm => trimmed.ToLowerInvariant(),
            PackageProvider.DockerHub => trimmed.ToLowerInvariant(),
            PackageProvider.CratesIo => trimmed,
            _ => trimmed,
        };
    }

    /// <summary>Human-readable hint of the expected coordinate shape, used in API validation errors.</summary>
    public static string DescribeExpected(PackageProvider provider) => provider switch
    {
        PackageProvider.DockerHub => "namespace/name (lowercase, e.g. argoproj/argocd or library/redis)",
        PackageProvider.Npm => "package name (lowercase; may be scoped, e.g. react or @types/node)",
        PackageProvider.NuGet => "package id (letters/digits with . or -, case-insensitive, e.g. Newtonsoft.Json)",
        PackageProvider.PyPI => "project name (lowercase with . _ -, e.g. requests or django-allauth)",
        PackageProvider.CratesIo => "crate name (letters/digits with _ or -, e.g. serde or tokio)",
        _ => "valid package coordinate",
    };
}
