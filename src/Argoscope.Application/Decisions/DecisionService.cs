using System.Text.Json;
using Argoscope.Application.Collection;
using Argoscope.Domain.Common;
using Argoscope.Domain.Decisions;
using Argoscope.Domain.Portfolios;
using Argoscope.Domain.Repositories;

namespace Argoscope.Application.Decisions;

/// <summary>
/// Default actor id recorded on every revision. The argoscope MVP is
/// single-owner, so all journal entries are implicitly authored by
/// the same operator. The value is overridable per-call through the
/// command's <c>ActorId</c> field for tests and future multi-tenant
/// deployments.
/// </summary>
public static class DecisionActor
{
    public const string DefaultOwner = "owner";
}

/// <summary>
/// Use cases for the decision-journal aggregate. The service is the
/// only writer of <see cref="DecisionRevision"/> rows; clients always
/// go through it to keep the audit log in sync with the entry's
/// <c>RevisionNumber</c>.
/// </summary>
public sealed class DecisionService
{
    private readonly IDecisionEntryStore _entries;
    private readonly IDecisionRevisionStore _revisions;
    private readonly IDecisionEvidenceStore _evidence;
    private readonly DecisionEvidenceResolver _resolver;
    private readonly IPortfolioRepository _portfolios;
    private readonly IMembershipStore _memberships;

    public DecisionService(
        IDecisionEntryStore entries,
        IDecisionRevisionStore revisions,
        IDecisionEvidenceStore evidence,
        DecisionEvidenceResolver resolver,
        IPortfolioRepository portfolios,
        IMembershipStore memberships)
    {
        _entries = entries;
        _revisions = revisions;
        _evidence = evidence;
        _resolver = resolver;
        _portfolios = portfolios;
        _memberships = memberships;
    }

    public async Task<Result<DecisionEntryDto>> CreateAsync(
        CreateDecisionCommand command,
        CancellationToken cancellationToken)
    {
        if (!TryParseDecisionType(command.DecisionType, out var decisionType, out var typeError))
        {
            return typeError;
        }

        var portfolioExists = await _portfolios
            .FindAsync(command.PortfolioId, cancellationToken)
            .ConfigureAwait(false);
        if (portfolioExists is null)
        {
            return Error.NotFound("Portfolio not found.");
        }

        // Idempotent retry: if a key was supplied and we have already
        // created an entry with that key, return the original.
        if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            var existing = await _entries
                .FindByIdempotencyKeyAsync(command.PortfolioId, command.IdempotencyKey, cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                return await BuildDtoAsync(existing, cancellationToken).ConfigureAwait(false);
            }
        }

        if (command.RepositoryId is { } repoId)
        {
            var membership = await _memberships
                .FindByRepositoryAsync(command.PortfolioId, repoId, cancellationToken)
                .ConfigureAwait(false);
            if (membership is null)
            {
                return Error.Validation("Repository is not part of this portfolio.", target: "repositoryId");
            }
        }

        var entryResult = TryCreateEntry(command, decisionType);
        if (entryResult.IsFailure) return entryResult.Error!.Value;

        await _entries.AddAsync(entryResult.Value, cancellationToken).ConfigureAwait(false);

        var evidenceResult = await PersistRevisionAsync(
            entryResult.Value,
            DecisionRevisionAction.Create,
            note: command.Note,
            evidence: command.Evidence,
            actorId: command.ActorId,
            occurredAtUtc: command.Now,
            before: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (evidenceResult.IsFailure) return evidenceResult.Error!.Value;

        return await BuildDtoAsync(entryResult.Value, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<DecisionEntryDto>> UpdateAsync(
        UpdateDecisionCommand command,
        CancellationToken cancellationToken)
    {
        if (!TryParseDecisionType(command.DecisionType, out var decisionType, out var typeError))
        {
            return typeError;
        }

        var entry = await _entries
            .FindAsync(command.DecisionEntryId, cancellationToken)
            .ConfigureAwait(false);
        if (entry is null || entry.PortfolioId != command.PortfolioId)
        {
            return Error.NotFound("Decision not found.");
        }
        if (entry.IsDeleted)
        {
            return Error.Conflict("Decision has been deleted; restore it before editing.");
        }
        if (entry.RevisionNumber != command.ExpectedRevision)
        {
            return Error.Conflict(
                $"Stale expectedRevision {command.ExpectedRevision}; current revision is {entry.RevisionNumber}.");
        }

        var before = DecisionRevisionSerializer.Serialize(entry);
        try
        {
            entry.ApplyUpdate(decisionType, command.DecisionDate, command.Rationale, command.ReviewDate, command.Now);
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }
        await _entries.UpdateAsync(entry, cancellationToken).ConfigureAwait(false);

        var evidenceResult = await PersistRevisionAsync(
            entry,
            DecisionRevisionAction.Update,
            note: command.Note,
            evidence: command.Evidence,
            actorId: command.ActorId,
            occurredAtUtc: command.Now,
            before: before,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (evidenceResult.IsFailure) return evidenceResult.Error!.Value;

        return await BuildDtoAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<DecisionEntryDto>> DeleteAsync(
        DeleteDecisionCommand command,
        CancellationToken cancellationToken)
    {
        var entry = await _entries
            .FindAsync(command.DecisionEntryId, cancellationToken)
            .ConfigureAwait(false);
        if (entry is null || entry.PortfolioId != command.PortfolioId)
        {
            return Error.NotFound("Decision not found.");
        }
        if (entry.IsDeleted)
        {
            return Error.Conflict("Decision is already deleted.");
        }
        if (entry.RevisionNumber != command.ExpectedRevision)
        {
            return Error.Conflict(
                $"Stale expectedRevision {command.ExpectedRevision}; current revision is {entry.RevisionNumber}.");
        }

        var before = DecisionRevisionSerializer.Serialize(entry);
        try
        {
            entry.MarkDeleted(command.Now);
        }
        catch (DomainException ex)
        {
            return Error.Conflict(ex.Message);
        }
        await _entries.UpdateAsync(entry, cancellationToken).ConfigureAwait(false);

        // The delete revision does not carry a fresh evidence
        // manifest — the manifest belongs to the last live revision
        // and we keep it there so the timeline shows what evidence
        // the owner had when the decision was live.
        var revision = new DecisionRevision(
            entry.Id,
            entry.RevisionNumber,
            DecisionRevisionAction.Delete,
            command.ActorId,
            command.Now,
            before,
            DecisionRevisionSerializer.Serialize(entry),
            command.Note);
        await _revisions.AddAsync(revision, cancellationToken).ConfigureAwait(false);

        return await BuildDtoAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<DecisionEntryDto>> RestoreAsync(
        RestoreDecisionCommand command,
        CancellationToken cancellationToken)
    {
        var entry = await _entries
            .FindAsync(command.DecisionEntryId, cancellationToken)
            .ConfigureAwait(false);
        if (entry is null || entry.PortfolioId != command.PortfolioId)
        {
            return Error.NotFound("Decision not found.");
        }
        if (!entry.IsDeleted)
        {
            return Error.Conflict("Decision is not deleted.");
        }
        if (entry.RevisionNumber != command.ExpectedRevision)
        {
            return Error.Conflict(
                $"Stale expectedRevision {command.ExpectedRevision}; current revision is {entry.RevisionNumber}.");
        }

        var before = DecisionRevisionSerializer.Serialize(entry);
        try
        {
            entry.MarkRestored(command.Now);
        }
        catch (DomainException ex)
        {
            return Error.Conflict(ex.Message);
        }
        await _entries.UpdateAsync(entry, cancellationToken).ConfigureAwait(false);

        var revision = new DecisionRevision(
            entry.Id,
            entry.RevisionNumber,
            DecisionRevisionAction.Restore,
            command.ActorId,
            command.Now,
            before,
            DecisionRevisionSerializer.Serialize(entry),
            command.Note);
        await _revisions.AddAsync(revision, cancellationToken).ConfigureAwait(false);

        return await BuildDtoAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DecisionEntryDto>> ListByPortfolioAsync(
        Id<Portfolio> portfolioId,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        var entries = await _entries
            .ListByPortfolioAsync(portfolioId, includeDeleted, cancellationToken)
            .ConfigureAwait(false);
        var dtos = new List<DecisionEntryDto>(entries.Count);
        foreach (var e in entries)
        {
            dtos.Add(await BuildDtoAsync(e, cancellationToken).ConfigureAwait(false));
        }
        return dtos;
    }

    public async Task<IReadOnlyList<DecisionEntryDto>> ListByRepositoryAsync(
        Id<Repository> repositoryId,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        var entries = await _entries
            .ListByRepositoryAsync(repositoryId, includeDeleted, cancellationToken)
            .ConfigureAwait(false);
        var dtos = new List<DecisionEntryDto>(entries.Count);
        foreach (var e in entries)
        {
            dtos.Add(await BuildDtoAsync(e, cancellationToken).ConfigureAwait(false));
        }
        return dtos;
    }

    public async Task<DecisionEntryDto?> GetAsync(
        Id<Portfolio> portfolioId,
        Id<DecisionEntry> decisionId,
        CancellationToken cancellationToken)
    {
        var entry = await _entries.FindAsync(decisionId, cancellationToken).ConfigureAwait(false);
        if (entry is null || entry.PortfolioId != portfolioId) return null;
        return await BuildDtoAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DecisionRevisionDto>> GetRevisionsAsync(
        Id<DecisionEntry> decisionId,
        CancellationToken cancellationToken)
    {
        var revisions = await _revisions
            .ListByEntryAsync(decisionId, cancellationToken)
            .ConfigureAwait(false);
        if (revisions.Count == 0) return Array.Empty<DecisionRevisionDto>();

        var revisionIds = revisions.Select(r => r.Id).ToList();
        var evidenceByRevision = await _evidence
            .ListByRevisionsAsync(revisionIds, cancellationToken)
            .ConfigureAwait(false);

        var dtos = new List<DecisionRevisionDto>(revisions.Count);
        foreach (var r in revisions)
        {
            var evidence = evidenceByRevision.TryGetValue(r.Id, out var list)
                ? list
                : (IReadOnlyList<DecisionEvidenceReference>)Array.Empty<DecisionEvidenceReference>();
            dtos.Add(await BuildRevisionDtoAsync(r, evidence, cancellationToken).ConfigureAwait(false));
        }
        return dtos;
    }

    private async Task<Result> PersistRevisionAsync(
        DecisionEntry entry,
        DecisionRevisionAction action,
        string? note,
        IReadOnlyList<CreateDecisionEvidenceCommand> evidence,
        string actorId,
        DateTimeOffset occurredAtUtc,
        string? before,
        CancellationToken cancellationToken)
    {
        var revision = new DecisionRevision(
            entry.Id,
            entry.RevisionNumber,
            action,
            actorId,
            occurredAtUtc,
            before,
            DecisionRevisionSerializer.Serialize(entry),
            note);
        await _revisions.AddAsync(revision, cancellationToken).ConfigureAwait(false);

        if (evidence is { Count: > 0 })
        {
            var evidenceRows = new List<DecisionEvidenceReference>(evidence.Count);
            foreach (var item in evidence)
            {
                if (!TryParseEvidenceKind(item.Kind, out var kind, out var kindError))
                {
                    return kindError;
                }
                if (item.ReferenceId == Guid.Empty)
                {
                    return Error.Validation("Evidence referenceId is required.", target: "evidence");
                }
                var row = new DecisionEvidenceReference(revision.Id, kind, item.ReferenceId, item.Label);
                evidenceRows.Add(row);
            }
            await _evidence.AddRangeAsync(evidenceRows, cancellationToken).ConfigureAwait(false);
        }
        return Result.Success();
    }

    private async Task<DecisionEntryDto> BuildDtoAsync(DecisionEntry entry, CancellationToken cancellationToken)
    {
        var latest = await _revisions
            .GetLatestAsync(entry.Id, cancellationToken)
            .ConfigureAwait(false);
        var evidence = latest is null
            ? (IReadOnlyList<DecisionEvidenceReference>)Array.Empty<DecisionEvidenceReference>()
            : await _evidence.ListByRevisionAsync(latest.Id, cancellationToken).ConfigureAwait(false);
        var evidenceDtos = new List<DecisionEvidenceDto>(evidence.Count);
        foreach (var e in evidence)
        {
            evidenceDtos.Add(await BuildEvidenceDtoAsync(entry.PortfolioId, e, cancellationToken).ConfigureAwait(false));
        }
        var summary = latest is null
            ? new DecisionRevisionSummaryDto(entry.RevisionNumber, "Create", "system", entry.CreatedAtUtc, null)
            : new DecisionRevisionSummaryDto(
                latest.RevisionNumber, latest.Action.ToString(), latest.ActorId, latest.OccurredAtUtc, latest.Note);
        return new DecisionEntryDto(
            entry.Id.Value,
            entry.PortfolioId.Value,
            entry.RepositoryId?.Value,
            entry.DecisionType.ToString(),
            entry.DecisionDate,
            entry.Rationale,
            entry.ReviewDate,
            entry.RevisionNumber,
            entry.IdempotencyKey,
            entry.DeletedAtUtc,
            entry.CreatedAtUtc,
            entry.UpdatedAtUtc,
            evidenceDtos,
            summary);
    }

    private async Task<DecisionRevisionDto> BuildRevisionDtoAsync(
        DecisionRevision revision,
        IReadOnlyList<DecisionEvidenceReference> evidence,
        CancellationToken cancellationToken)
    {
        var entry = await _entries
            .FindAsync(revision.DecisionEntryId, cancellationToken)
            .ConfigureAwait(false);
        var portfolioId = entry?.PortfolioId ?? default;
        var evidenceDtos = new List<DecisionEvidenceDto>(evidence.Count);
        foreach (var e in evidence)
        {
            evidenceDtos.Add(await BuildEvidenceDtoAsync(portfolioId, e, cancellationToken).ConfigureAwait(false));
        }
        return new DecisionRevisionDto(
            revision.RevisionNumber,
            revision.Action.ToString(),
            revision.ActorId,
            revision.OccurredAtUtc,
            revision.Note,
            evidenceDtos);
    }

    private async Task<DecisionEvidenceDto> BuildEvidenceDtoAsync(
        Id<Portfolio> portfolioId,
        DecisionEvidenceReference reference,
        CancellationToken cancellationToken)
    {
        var target = await _resolver
            .ResolveAsync(portfolioId, reference.Kind, reference.ReferenceId, cancellationToken)
            .ConfigureAwait(false);
        return new DecisionEvidenceDto(
            reference.Id.Value,
            reference.Kind.ToString(),
            reference.ReferenceId,
            target.Resolution.ToString(),
            target.Destination,
            reference.Label,
            target.EvidenceDate);
    }

    private static bool TryParseDecisionType(string raw, out DecisionType parsed, out Error error)
    {
        if (Enum.TryParse<DecisionType>(raw, ignoreCase: true, out parsed))
        {
            error = default;
            return true;
        }
        parsed = default;
        error = Error.Validation(
            $"DecisionType must be one of: {string.Join(", ", Enum.GetNames<DecisionType>())}.",
            target: "decisionType");
        return false;
    }

    private static bool TryParseEvidenceKind(string raw, out DecisionEvidenceKind parsed, out Error error)
    {
        if (Enum.TryParse<DecisionEvidenceKind>(raw, ignoreCase: true, out parsed))
        {
            error = default;
            return true;
        }
        parsed = default;
        error = Error.Validation(
            $"Evidence kind must be one of: {string.Join(", ", Enum.GetNames<DecisionEvidenceKind>())}.",
            target: "evidenceKind");
        return false;
    }

    private static Result<DecisionEntry> TryCreateEntry(
        CreateDecisionCommand command,
        DecisionType decisionType)
    {
        try
        {
            var entry = new DecisionEntry(
                command.PortfolioId,
                command.RepositoryId,
                decisionType,
                command.DecisionDate,
                command.Rationale,
                command.ReviewDate,
                command.IdempotencyKey,
                command.Now);
            return Result<DecisionEntry>.Success(entry);
        }
        catch (DomainException ex)
        {
            return Result<DecisionEntry>.Failure(Error.Validation(ex.Message));
        }
    }
}
