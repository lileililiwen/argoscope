using Argoscope.Application.Signals;
using Argoscope.Domain.Common;
using Argoscope.Domain.Repositories;
using Argoscope.Domain.Signals;
using Microsoft.EntityFrameworkCore;

namespace Argoscope.Infrastructure.Persistence;

public sealed class EfCommercialSignalStore : ICommercialSignalStore
{
    private readonly ArgoscopeDbContext _db;
    public EfCommercialSignalStore(ArgoscopeDbContext db) => _db = db;

    public Task<CommercialSignal?> FindAsync(Id<CommercialSignal> id, CancellationToken cancellationToken) =>
        _db.CommercialSignals.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<CommercialSignal?> FindByContentHashAsync(
        Id<Repository> repositoryId, SignalSourceType sourceType, int sourceNumber, string contentHash,
        CancellationToken cancellationToken) =>
        _db.CommercialSignals.FirstOrDefaultAsync(
            s => s.RepositoryId == repositoryId && s.SourceType == sourceType
                && s.SourceNumber == sourceNumber && s.ContentHash == contentHash,
            cancellationToken);

    public async Task<IReadOnlyList<CommercialSignal>> ListBySourceAsync(
        Id<Repository> repositoryId, SignalSourceType sourceType, int sourceNumber,
        CancellationToken cancellationToken) =>
        await _db.CommercialSignals
            .Where(s => s.RepositoryId == repositoryId && s.SourceType == sourceType && s.SourceNumber == sourceNumber)
            .OrderBy(s => s.SuggestionVersion)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<CommercialSignal>> ListByRepositoryAsync(
        Id<Repository> repositoryId, string? state, CancellationToken cancellationToken)
    {
        var query = _db.CommercialSignals.Where(s => s.RepositoryId == repositoryId);
        if (!string.IsNullOrWhiteSpace(state))
        {
            if (string.Equals(state, "pending", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(s => s.Status == SignalStatus.Pending || s.Status == SignalStatus.NeedsRetry);
            }
            else if (string.Equals(state, "reviewed", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(s => s.Status == SignalStatus.Accepted || s.Status == SignalStatus.Rejected || s.Status == SignalStatus.Corrected);
            }
        }
        return await query
            .OrderByDescending(s => s.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(CommercialSignal signal, CancellationToken cancellationToken)
    {
        await _db.CommercialSignals.AddAsync(signal, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(CommercialSignal signal, CancellationToken cancellationToken)
    {
        _db.CommercialSignals.Update(signal);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class EfSignalReviewStore : ISignalReviewStore
{
    private readonly ArgoscopeDbContext _db;
    public EfSignalReviewStore(ArgoscopeDbContext db) => _db = db;

    public async Task AddAsync(SignalReview review, CancellationToken cancellationToken)
    {
        await _db.SignalReviews.AddAsync(review, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SignalReview>> ListBySignalAsync(
        Id<CommercialSignal> signalId, CancellationToken cancellationToken) =>
        await _db.SignalReviews
            .Where(r => r.SignalId == signalId)
            .OrderBy(r => r.RevisionNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}
