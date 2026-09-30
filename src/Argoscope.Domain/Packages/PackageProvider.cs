using System.Text.Json.Serialization;
using Argoscope.Domain.Common;

namespace Argoscope.Domain.Packages;

/// <summary>
/// External package registries Argoscope can observe. The enumeration is the
/// single source of truth for the provider column; adding a new registry
/// requires extending the validator and the provider adapter together.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PackageProvider
{
    DockerHub = 0,
    Npm = 1,
    NuGet = 2,
    PyPI = 3,
    CratesIo = 4,
}

/// <summary>
/// Provider-reported measurement unit. Different registries use different
/// units (pulls, downloads); Argoscope never combines unlike units into a
/// single series — the (provider, unit, window) tuple is the series key.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PackageUnit
{
    Pulls = 0,
    Downloads = 1,
}

/// <summary>
/// Provider-reported aggregation window. Different registries report
/// different windows (cumulative, daily, weekly, monthly). Like units,
/// windows are part of the series identity and are never mixed.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PackageWindow
{
    Cumulative = 0,
    Daily = 1,
    Weekly = 2,
    Monthly = 3,
}

/// <summary>
/// Lifecycle of an owner-confirmed package association. The status is
/// driven by the application layer: <see cref="Linked"/> when the
/// association is active; <see cref="AttentionRequired"/> when the
/// provider signals a deleted or renamed package; <see cref="Removed"/>
/// when the owner explicitly removes the association. Historical
/// observations are always retained.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PackageAssociationStatus
{
    Linked = 0,
    AttentionRequired = 1,
    Removed = 2,
}

/// <summary>
/// Default unit for each provider. Used as the association's initial
/// DefaultUnit at creation time; the owner can override per-association.
/// </summary>
public static class PackageUnitDefaults
{
    public static readonly IReadOnlyDictionary<PackageProvider, PackageUnit> ByProvider =
        new Dictionary<PackageProvider, PackageUnit>
        {
            [PackageProvider.DockerHub] = PackageUnit.Pulls,
            [PackageProvider.Npm] = PackageUnit.Downloads,
            [PackageProvider.NuGet] = PackageUnit.Downloads,
            [PackageProvider.PyPI] = PackageUnit.Downloads,
            [PackageProvider.CratesIo] = PackageUnit.Downloads,
        };

    public static readonly IReadOnlyDictionary<PackageProvider, PackageWindow> DefaultWindowByProvider =
        new Dictionary<PackageProvider, PackageWindow>
        {
            [PackageProvider.DockerHub] = PackageWindow.Cumulative,
            [PackageProvider.Npm] = PackageWindow.Weekly,
            [PackageProvider.NuGet] = PackageWindow.Cumulative,
            [PackageProvider.PyPI] = PackageWindow.Daily,
            [PackageProvider.CratesIo] = PackageWindow.Cumulative,
        };
}
