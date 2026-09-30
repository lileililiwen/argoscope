using System.Text.Json;
using System.Text.Json.Serialization;
using Argoscope.Domain.Common;
using Argoscope.Domain.Decisions;

namespace Argoscope.Application.Decisions;

/// <summary>
/// Round-trips <see cref="DecisionEntry"/> through JSON so the
/// <see cref="DecisionRevision"/> before/after images can be stored as
/// stable, append-only audit text. The format is plain System.Text.Json
/// without reference handling; the resulting JSON is treated as opaque
/// by the rest of the system.
/// </summary>
public static class DecisionRevisionSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter(), new IdJsonConverter() },
    };

    public static string Serialize(DecisionEntry entry) =>
        JsonSerializer.Serialize(ToSnapshot(entry), Options);

    public static string? SerializeOrNull(DecisionEntry? entry) =>
        entry is null ? null : Serialize(entry);

    /// <summary>
    /// Project a domain entity into a flat record so the audit JSON is
    /// stable across refactors of the entity (private setters and EF
    /// shadow properties do not leak into the audit log).
    /// </summary>
    public static DecisionEntrySnapshot ToSnapshot(DecisionEntry entry) =>
        new(
            entry.Id.Value,
            entry.PortfolioId.Value,
            entry.RepositoryId?.Value,
            entry.DecisionType,
            entry.DecisionDate,
            entry.Rationale,
            entry.ReviewDate,
            entry.RevisionNumber,
            entry.IdempotencyKey,
            entry.DeletedAtUtc,
            entry.CreatedAtUtc,
            entry.UpdatedAtUtc);

    public sealed record DecisionEntrySnapshot(
        Guid DecisionEntryId,
        Guid PortfolioId,
        Guid? RepositoryId,
        DecisionType DecisionType,
        DateOnly DecisionDate,
        string Rationale,
        DateOnly? ReviewDate,
        int RevisionNumber,
        string? IdempotencyKey,
        DateTimeOffset? DeletedAtUtc,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc);
}
