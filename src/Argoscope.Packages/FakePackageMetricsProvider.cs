using Argoscope.Domain.Packages;
using Argoscope.Domain.Snapshots;

namespace Argoscope.Packages;

/// <summary>
/// In-memory deterministic provider used by unit and integration tests.
/// Each instance serves exactly one <see cref="PackageProvider"/>.
/// Configures the same way the GitHub fake does: register fixtures,
/// optionally override per-coordinate status, then assert against the
/// recorded calls. No HTTP or external IO is performed.
/// </summary>
public sealed class FakePackageMetricsProvider : IPackageMetricsProvider
{
    public string ProviderVersion { get; }

    public PackageProvider Provider { get; }

    public sealed record ObservationFixture(
        string Coordinate,
        PackageUnit Unit,
        PackageWindow Window,
        DateTimeOffset WindowStartUtc,
        DateTimeOffset WindowEndUtc,
        double Value,
        DateTimeOffset ObservedAtUtc);

    public sealed record ExistenceFixture(string Coordinate, bool Exists);

    private readonly Dictionary<string, List<ObservationFixture>> _observations = new(StringComparer.Ordinal);
    private readonly HashSet<string> _exists = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProviderResultStatus> _metadataStatuses = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProviderResultStatus> _pageStatuses = new(StringComparer.Ordinal);
    private readonly List<(string Coordinate, PackageUnit Unit, PackageWindow Window, string? Cursor)> _pageCalls = new();
    private readonly List<string> _probeCalls = new();

    public IReadOnlyList<(string Coordinate, PackageUnit Unit, PackageWindow Window, string? Cursor)> PageCalls => _pageCalls;
    public IReadOnlyList<string> ProbeCalls => _probeCalls;

    /// <summary>Optional map of coordinate -> provider status to return from <see cref="ProbeAsync"/>.</summary>
    public Dictionary<string, ProviderResultStatus> MetadataStatuses => _metadataStatuses;

    /// <summary>Optional map of "coord|unit|window" -> provider status for the next page call.</summary>
    public Dictionary<string, ProviderResultStatus> PageStatuses => _pageStatuses;

    public FakePackageMetricsProvider(PackageProvider provider, string providerVersion = "fake-1")
    {
        Provider = provider;
        ProviderVersion = providerVersion;
    }

    public void AddExistence(string coordinate, bool exists = true)
    {
        if (exists) _exists.Add(coordinate);
    }

    public void AddObservation(ObservationFixture fixture)
    {
        var key = fixture.Coordinate;
        if (!_observations.TryGetValue(key, out var list))
        {
            list = new List<ObservationFixture>();
            _observations[key] = list;
        }
        list.Add(fixture);
    }

    public Task<PackageMetadataResult> ProbeAsync(string coordinate, CancellationToken cancellationToken)
    {
        _probeCalls.Add(coordinate);
        if (_metadataStatuses.TryGetValue(coordinate, out var status))
        {
            return Task.FromResult(PackageMetadataResult.Failure(status, $"fake-status-{status}"));
        }
        if (_exists.Contains(coordinate))
        {
            return Task.FromResult(PackageMetadataResult.Ok(DateTimeOffset.UtcNow));
        }
        return Task.FromResult(PackageMetadataResult.NotFound("fake-not-found"));
    }

    public Task<PackageMetricPage> GetObservationsAsync(
        string coordinate,
        PackageUnit unit,
        PackageWindow window,
        DateTimeOffset sinceUtc,
        string? cursor,
        CancellationToken cancellationToken)
    {
        _pageCalls.Add((coordinate, unit, window, cursor));
        var pageKey = $"{coordinate}|{unit}|{window}";
        if (_pageStatuses.TryGetValue(pageKey, out var pageStatus)
            && pageStatus is not (ProviderResultStatus.Available or ProviderResultStatus.Partial))
        {
            return Task.FromResult(new PackageMetricPage(
                Array.Empty<PackageMetricObservation>(), null, pageStatus, null, $"fake-status-{pageStatus}"));
        }

        if (!_observations.TryGetValue(coordinate, out var fixtures))
        {
            return Task.FromResult(new PackageMetricPage(
                Array.Empty<PackageMetricObservation>(), null, ProviderResultStatus.Available, null, null));
        }

        var matching = fixtures
            .Where(f => f.Unit == unit && f.Window == window && f.WindowStartUtc >= sinceUtc)
            .OrderBy(f => f.WindowStartUtc)
            .ToList();

        const int pageSize = 30;
        var startIndex = ParseCursor(cursor);
        if (startIndex < 0 || startIndex > matching.Count)
        {
            startIndex = 0;
        }
        var slice = matching.Skip(startIndex).Take(pageSize).ToList();
        var endIndex = startIndex + slice.Count;
        var nextCursor = endIndex < matching.Count ? endIndex.ToString() : null;

        var items = slice.Select(f => new PackageMetricObservation(
            Provider, f.Unit, f.Window, f.WindowStartUtc, f.WindowEndUtc, f.Value, f.ObservedAtUtc)).ToList();

        return Task.FromResult(new PackageMetricPage(items, nextCursor, ProviderResultStatus.Available, null, null));
    }

    private static int ParseCursor(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor)) return 0;
        return int.TryParse(cursor, out var n) ? n : -1;
    }
}
