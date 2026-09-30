using Argoscope.Application.Collection;
using Argoscope.Application.Signals;
using Argoscope.Domain.Common;
using Argoscope.Domain.Portfolios;
using Argoscope.Domain.Repositories;
using Argoscope.Domain.Signals;
using Argoscope.GitHub;

namespace Argoscope.UnitTests;

public sealed class InMemoryCommercialSignalStore : ICommercialSignalStore
{
    private readonly Dictionary<Id<CommercialSignal>, CommercialSignal> _byId = new();

    public Task<CommercialSignal?> FindAsync(Id<CommercialSignal> id, CancellationToken cancellationToken) =>
        Task.FromResult(_byId.TryGetValue(id, out var v) ? v : null);

    public Task<CommercialSignal?> FindByContentHashAsync(
        Id<Repository> repositoryId, SignalSourceType sourceType, int sourceNumber, string contentHash,
        CancellationToken cancellationToken) =>
        Task.FromResult(_byId.Values.FirstOrDefault(s =>
            s.RepositoryId == repositoryId && s.SourceType == sourceType
            && s.SourceNumber == sourceNumber && s.ContentHash == contentHash));

    public Task<IReadOnlyList<CommercialSignal>> ListBySourceAsync(
        Id<Repository> repositoryId, SignalSourceType sourceType, int sourceNumber,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CommercialSignal>>(_byId.Values
            .Where(s => s.RepositoryId == repositoryId && s.SourceType == sourceType && s.SourceNumber == sourceNumber)
            .OrderBy(s => s.SuggestionVersion)
            .ToList());

    public Task<IReadOnlyList<CommercialSignal>> ListByRepositoryAsync(
        Id<Repository> repositoryId, string? state, CancellationToken cancellationToken)
    {
        IEnumerable<CommercialSignal> rows = _byId.Values.Where(s => s.RepositoryId == repositoryId);
        if (string.Equals(state, "pending", StringComparison.OrdinalIgnoreCase))
        {
            rows = rows.Where(s => s.Status == SignalStatus.Pending || s.Status == SignalStatus.NeedsRetry);
        }
        else if (string.Equals(state, "reviewed", StringComparison.OrdinalIgnoreCase))
        {
            rows = rows.Where(s => s.Status == SignalStatus.Accepted || s.Status == SignalStatus.Rejected || s.Status == SignalStatus.Corrected);
        }
        return Task.FromResult<IReadOnlyList<CommercialSignal>>(rows.OrderByDescending(s => s.CreatedAtUtc).ToList());
    }

    public Task AddAsync(CommercialSignal signal, CancellationToken cancellationToken)
    {
        _byId[signal.Id] = signal;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(CommercialSignal signal, CancellationToken cancellationToken)
    {
        _byId[signal.Id] = signal;
        return Task.CompletedTask;
    }
}

public sealed class InMemorySignalReviewStore : ISignalReviewStore
{
    private readonly List<SignalReview> _items = new();

    public Task AddAsync(SignalReview review, CancellationToken cancellationToken)
    {
        _items.Add(review);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SignalReview>> ListBySignalAsync(
        Id<CommercialSignal> signalId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SignalReview>>(_items
            .Where(r => r.SignalId == signalId)
            .OrderBy(r => r.RevisionNumber)
            .ToList());
}

public sealed class StubSourceProvider : ICommercialSourceProvider
{
    public List<CommercialSourceInput> Sources { get; } = new();

    public int Calls { get; private set; }

    public Task<IReadOnlyList<CommercialSourceInput>> ListSourcesAsync(
        string ownerLogin, string name, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult<IReadOnlyList<CommercialSourceInput>>(Sources.ToList());
    }
}

public sealed class MalformedClassifier : ISignalClassifier
{
    public string ClassifierVersion => "malformed-1";

    public Task<ClassifierResult> ClassifyAsync(string excerpt, CancellationToken cancellationToken) =>
        Task.FromResult(new ClassifierResult(true,
            new SignalClassification((SignalCategory)999, 42.0, ClassifierVersion, "bogus"), null));
}

public sealed class ThrowingClassifier : ISignalClassifier
{
    public string ClassifierVersion => "throwing-1";

    public Task<ClassifierResult> ClassifyAsync(string excerpt, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("model down");
}

public sealed class LowConfidenceClassifier : ISignalClassifier
{
    public string ClassifierVersion => "lowconf-1";

    public Task<ClassifierResult> ClassifyAsync(string excerpt, CancellationToken cancellationToken) =>
        Task.FromResult(new ClassifierResult(true,
            new SignalClassification(SignalCategory.HostedRequest, 0.2, ClassifierVersion, "weak hint"), null));
}
