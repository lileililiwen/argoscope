using Argoscope.Domain.Signals;

namespace Argoscope.GitHub;

/// <summary>Eligible issue/PR text handed to the collection service. Title/body only; comments excluded in v1.</summary>
public sealed record CommercialSourceInput(
    SignalSourceType SourceType,
    int SourceNumber,
    string SourceUrl,
    DateTimeOffset SourceUpdatedAtUtc,
    string Title,
    string Body);

/// <summary>
/// Read-only source of eligible issue/PR text. The real adapter performs no
/// writes; the fake serves fixtures. Private repositories require owner
/// authorization at the collection boundary.
/// </summary>
public interface ICommercialSourceProvider
{
    Task<IReadOnlyList<CommercialSourceInput>> ListSourcesAsync(
        string ownerLogin, string name, CancellationToken cancellationToken);
}
