using Argoscope.Domain.Common;
using Argoscope.Domain.Repositories;
using Argoscope.Domain.Signals;

namespace Argoscope.Application.Signals;

public interface ICommercialSignalStore
{
    Task<CommercialSignal?> FindAsync(Id<CommercialSignal> id, CancellationToken cancellationToken);

    Task<CommercialSignal?> FindByContentHashAsync(
        Id<Repository> repositoryId, SignalSourceType sourceType, int sourceNumber, string contentHash,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CommercialSignal>> ListBySourceAsync(
        Id<Repository> repositoryId, SignalSourceType sourceType, int sourceNumber,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CommercialSignal>> ListByRepositoryAsync(
        Id<Repository> repositoryId, string? state, CancellationToken cancellationToken);

    Task AddAsync(CommercialSignal signal, CancellationToken cancellationToken);

    Task UpdateAsync(CommercialSignal signal, CancellationToken cancellationToken);
}

public interface ISignalReviewStore
{
    Task AddAsync(SignalReview review, CancellationToken cancellationToken);

    Task<IReadOnlyList<SignalReview>> ListBySignalAsync(
        Id<CommercialSignal> signalId, CancellationToken cancellationToken);
}
