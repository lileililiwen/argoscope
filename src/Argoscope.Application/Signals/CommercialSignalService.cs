using Argoscope.Application.Collection;
using Argoscope.Domain.Common;
using Argoscope.Domain.Portfolios;
using Argoscope.Domain.Repositories;
using Argoscope.Domain.Signals;
using Argoscope.GitHub;

namespace Argoscope.Application.Signals;

/// <summary>
/// Commercial-signal use cases. Collection is idempotent per content hash;
/// changed content creates a new pending suggestion version without touching
/// prior human decisions. Reviews are optimistic-concurrency guarded and
/// append an immutable audit row. Nothing here alters scores, ranks,
/// lifecycle state or GitHub resources.
/// </summary>
public sealed class CommercialSignalService
{
    private readonly ICommercialSignalStore _signals;
    private readonly ISignalReviewStore _reviews;
    private readonly ICommercialSourceProvider _sources;
    private readonly ISignalClassifier _classifier;
    private readonly IRepositoryStore _repositories;

    public CommercialSignalService(
        ICommercialSignalStore signals,
        ISignalReviewStore reviews,
        ICommercialSourceProvider sources,
        ISignalClassifier classifier,
        IRepositoryStore repositories)
    {
        _signals = signals;
        _reviews = reviews;
        _sources = sources;
        _classifier = classifier;
        _repositories = repositories;
    }

    public async Task<Result<CollectSignalsResult>> CollectAsync(
        CollectSignalsCommand command, CancellationToken cancellationToken)
    {
        var repo = await _repositories.FindAsync(command.RepositoryId, cancellationToken).ConfigureAwait(false);
        if (repo is null) return Error.NotFound("Repository not found.");

        IReadOnlyList<CommercialSourceInput> sources;
        try
        {
            sources = await _sources.ListSourcesAsync(repo.OwnerLogin, repo.Name, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Error.Validation($"Source provider unavailable (retryable): {ex.Message}");
        }

        var created = 0;
        var duplicates = 0;
        var retried = 0;
        var seen = new HashSet<(SignalSourceType, int)>();
        foreach (var s in sources)
        {
            seen.Add((s.SourceType, s.SourceNumber));
            if (!SignalTextProcessor.HasUsableText(s.Title, s.Body)) continue;

            var excerpt = SignalTextProcessor.BuildExcerpt(s.Title, s.Body);
            var hash = SignalTextProcessor.ContentHash(s.SourceType.ToString(), s.SourceNumber, s.SourceUpdatedAtUtc, excerpt);
            var existing = await _signals.FindByContentHashAsync(
                command.RepositoryId, s.SourceType, s.SourceNumber, hash, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                duplicates++;
                continue;
            }

            var lineage = await _signals.ListBySourceAsync(
                command.RepositoryId, s.SourceType, s.SourceNumber, cancellationToken).ConfigureAwait(false);
            var nextVersion = lineage.Count == 0 ? 1 : lineage.Max(x => x.SuggestionVersion) + 1;

            ClassifierResult outcome;
            try
            {
                outcome = await _classifier.ClassifyAsync(excerpt, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                outcome = new ClassifierResult(false, null, "classifier-exception");
            }

            CommercialSignal signal;
            try
            {
                if (!outcome.IsSuccess || outcome.Classification is null)
                {
                    signal = new CommercialSignal(
                        command.RepositoryId, s.SourceType, s.SourceNumber, s.SourceUrl,
                        s.SourceUpdatedAtUtc, hash, excerpt, nextVersion,
                        SignalCategory.Unclassified, 0, _classifier.ClassifierVersion,
                        "Classifier unavailable; retryable.",
                        SignalStatus.NeedsRetry, command.Now);
                    retried++;
                }
                else
                {
                    var c = outcome.Classification;
                    if (!Enum.IsDefined(c.Category) || c.Confidence is < 0 or > 1)
                    {
                        signal = new CommercialSignal(
                            command.RepositoryId, s.SourceType, s.SourceNumber, s.SourceUrl,
                            s.SourceUpdatedAtUtc, hash, excerpt, nextVersion,
                            SignalCategory.Unclassified, 0, _classifier.ClassifierVersion,
                            $"Rejected malformed classifier output ({c.Category}/{c.Confidence}); retryable.",
                            SignalStatus.NeedsRetry, command.Now);
                        retried++;
                    }
                    else
                    {
                        var category = c.Confidence < KeywordSignalClassifier.LowConfidenceFloor
                            ? SignalCategory.Unclear
                            : c.Category;
                        var rationale = c.Rationale.Length > CommercialSignal.MaxRationaleLength
                            ? c.Rationale.Substring(0, CommercialSignal.MaxRationaleLength)
                            : c.Rationale;
                        signal = new CommercialSignal(
                            command.RepositoryId, s.SourceType, s.SourceNumber, s.SourceUrl,
                            s.SourceUpdatedAtUtc, hash, excerpt, nextVersion,
                            category, category == SignalCategory.Unclear && c.Category != SignalCategory.Unclear ? c.Confidence : c.Confidence,
                            _classifier.ClassifierVersion, rationale,
                            SignalStatus.Pending, command.Now);
                        created++;
                    }
                }
            }
            catch (DomainException ex)
            {
                return Error.Validation(ex.Message);
            }
            await _signals.AddAsync(signal, cancellationToken).ConfigureAwait(false);
        }

        // Sources that disappeared upstream: retain audit, mark unavailable.
        var marked = 0;
        var all = await _signals.ListByRepositoryAsync(command.RepositoryId, null, cancellationToken).ConfigureAwait(false);
        foreach (var row in all)
        {
            if (row.SourceAvailable && !seen.Contains((row.SourceType, row.SourceNumber)))
            {
                row.MarkSourceUnavailable(command.Now);
                await _signals.UpdateAsync(row, cancellationToken).ConfigureAwait(false);
                marked++;
            }
        }

        return Result<CollectSignalsResult>.Success(new CollectSignalsResult(created, duplicates, retried, marked));
    }

    public async Task<IReadOnlyList<CommercialSignalDto>> ListAsync(
        Id<Repository> repositoryId, string? state, CancellationToken cancellationToken)
    {
        var rows = await _signals.ListByRepositoryAsync(repositoryId, state, cancellationToken).ConfigureAwait(false);
        return rows.Select(ToDto).ToList();
    }

    public async Task<CommercialSignalDto?> GetAsync(
        Id<Repository> repositoryId, Id<CommercialSignal> signalId, CancellationToken cancellationToken)
    {
        var row = await _signals.FindAsync(signalId, cancellationToken).ConfigureAwait(false);
        if (row is null || row.RepositoryId != repositoryId) return null;
        return ToDto(row);
    }

    public async Task<IReadOnlyList<SignalReviewDto>> GetReviewsAsync(
        Id<Repository> repositoryId, Id<CommercialSignal> signalId, CancellationToken cancellationToken)
    {
        var row = await _signals.FindAsync(signalId, cancellationToken).ConfigureAwait(false);
        if (row is null || row.RepositoryId != repositoryId) return Array.Empty<SignalReviewDto>();
        var reviews = await _reviews.ListBySignalAsync(signalId, cancellationToken).ConfigureAwait(false);
        return reviews
            .Select(r => new SignalReviewDto(
                r.RevisionNumber, r.Decision.ToString(), r.PriorCategory.ToString(),
                r.PriorStatus.ToString(), r.CorrectedCategory?.ToString(), r.Reviewer,
                r.OccurredAtUtc, r.Note))
            .ToList();
    }

    public async Task<Result<CommercialSignalDto>> ReviewAsync(
        ReviewSignalCommand command, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ReviewDecision>(command.Decision, ignoreCase: true, out var decision))
        {
            return Error.Validation(
                $"Decision must be one of: {string.Join(", ", Enum.GetNames<ReviewDecision>())}.",
                target: "decision");
        }
        SignalCategory? corrected = null;
        if (!string.IsNullOrWhiteSpace(command.CorrectedCategory))
        {
            if (!Enum.TryParse<SignalCategory>(command.CorrectedCategory, ignoreCase: true, out var parsed))
            {
                return Error.Validation(
                    $"CorrectedCategory must be one of: {string.Join(", ", Enum.GetNames<SignalCategory>())}.",
                    target: "correctedCategory");
            }
            corrected = parsed;
        }

        var row = await _signals.FindAsync(command.SignalId, cancellationToken).ConfigureAwait(false);
        if (row is null || row.RepositoryId != command.RepositoryId)
        {
            return Error.NotFound("Signal not found.");
        }
        if (row.Version != command.ExpectedVersion)
        {
            return Error.Conflict(
                $"Stale expectedVersion {command.ExpectedVersion}; current version is {row.Version}.");
        }

        var priorCategory = row.Category;
        var priorStatus = row.Status;
        try
        {
            row.ApplyReview(decision, corrected, command.Reviewer, command.Now);
        }
        catch (DomainException ex)
        {
            var code = ex.Message.Contains("already", StringComparison.OrdinalIgnoreCase) ? "conflict" : "validation";
            return code == "conflict" ? Error.Conflict(ex.Message) : Error.Validation(ex.Message);
        }
        await _signals.UpdateAsync(row, cancellationToken).ConfigureAwait(false);
        await _reviews.AddAsync(new SignalReview(
            row.Id, row.Version, decision, priorCategory, priorStatus,
            row.CorrectedCategory, command.Reviewer, command.Now, command.Note), cancellationToken).ConfigureAwait(false);
        return Result<CommercialSignalDto>.Success(ToDto(row));
    }

    private static CommercialSignalDto ToDto(CommercialSignal s) => new(
        s.Id.Value, s.RepositoryId.Value, s.SourceType.ToString(), s.SourceNumber,
        s.SourceUrl, s.SourceUpdatedAtUtc, s.ContentHash, s.Excerpt, s.SourceAvailable,
        s.SuggestionVersion, s.Category.ToString(), s.Confidence, s.ClassifierVersion,
        s.Rationale, s.Status.ToString(), s.CorrectedCategory?.ToString(), s.Reviewer,
        s.ReviewedAtUtc, s.Version, s.CreatedAtUtc, s.UpdatedAtUtc);
}
