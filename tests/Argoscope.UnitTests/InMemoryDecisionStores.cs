using Argoscope.Application.Decisions;
using Argoscope.Domain.Common;
using Argoscope.Domain.Decisions;
using Argoscope.Domain.Portfolios;
using Argoscope.Domain.Repositories;

namespace Argoscope.UnitTests;

/// <summary>Thread-unsafe in-memory test double for <see cref="IDecisionEntryStore"/>.</summary>
public sealed class InMemoryDecisionEntryStore : IDecisionEntryStore
{
    private readonly Dictionary<Id<DecisionEntry>, DecisionEntry> _byId = new();
    private readonly object _lock = new();

    public Task<DecisionEntry?> FindAsync(Id<DecisionEntry> id, CancellationToken cancellationToken)
    {
        lock (_lock) { return Task.FromResult(_byId.TryGetValue(id, out var v) ? v : null); }
    }

    public Task<DecisionEntry?> FindByIdempotencyKeyAsync(
        Id<Portfolio> portfolioId, string idempotencyKey, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult(_byId.Values.FirstOrDefault(d =>
                d.PortfolioId == portfolioId && d.IdempotencyKey == idempotencyKey));
        }
    }

    public Task<IReadOnlyList<DecisionEntry>> ListByPortfolioAsync(
        Id<Portfolio> portfolioId, bool includeDeleted, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var rows = _byId.Values
                .Where(d => d.PortfolioId == portfolioId)
                .Where(d => includeDeleted || d.DeletedAtUtc is null)
                .OrderByDescending(d => d.DecisionDate)
                .ThenByDescending(d => d.CreatedAtUtc)
                .ToList();
            return Task.FromResult<IReadOnlyList<DecisionEntry>>(rows);
        }
    }

    public Task<IReadOnlyList<DecisionEntry>> ListByRepositoryAsync(
        Id<Repository> repositoryId, bool includeDeleted, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var rows = _byId.Values
                .Where(d => d.RepositoryId == repositoryId)
                .Where(d => includeDeleted || d.DeletedAtUtc is null)
                .OrderByDescending(d => d.DecisionDate)
                .ThenByDescending(d => d.CreatedAtUtc)
                .ToList();
            return Task.FromResult<IReadOnlyList<DecisionEntry>>(rows);
        }
    }

    public Task<DecisionEntry> AddAsync(DecisionEntry entry, CancellationToken cancellationToken)
    {
        lock (_lock) { _byId[entry.Id] = entry; }
        return Task.FromResult(entry);
    }

    public Task UpdateAsync(DecisionEntry entry, CancellationToken cancellationToken)
    {
        lock (_lock) { _byId[entry.Id] = entry; }
        return Task.CompletedTask;
    }
}

/// <summary>Thread-unsafe in-memory test double for <see cref="IDecisionRevisionStore"/>.</summary>
public sealed class InMemoryDecisionRevisionStore : IDecisionRevisionStore
{
    private readonly List<DecisionRevision> _items = new();
    private readonly object _lock = new();

    public Task<DecisionRevision> AddAsync(DecisionRevision revision, CancellationToken cancellationToken)
    {
        lock (_lock) { _items.Add(revision); }
        return Task.FromResult(revision);
    }

    public Task<IReadOnlyList<DecisionRevision>> ListByEntryAsync(
        Id<DecisionEntry> decisionEntryId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<DecisionRevision>>(
                _items.Where(r => r.DecisionEntryId == decisionEntryId)
                    .OrderBy(r => r.RevisionNumber)
                    .ToList());
        }
    }

    public Task<DecisionRevision?> GetLatestAsync(
        Id<DecisionEntry> decisionEntryId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult(_items
                .Where(r => r.DecisionEntryId == decisionEntryId)
                .OrderByDescending(r => r.RevisionNumber)
                .FirstOrDefault());
        }
    }
}

/// <summary>Thread-unsafe in-memory test double for <see cref="IDecisionEvidenceStore"/>.</summary>
public sealed class InMemoryDecisionEvidenceStore : IDecisionEvidenceStore
{
    private readonly List<DecisionEvidenceReference> _items = new();
    private readonly object _lock = new();

    public Task AddRangeAsync(
        IReadOnlyList<DecisionEvidenceReference> references, CancellationToken cancellationToken)
    {
        lock (_lock) { _items.AddRange(references); }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DecisionEvidenceReference>> ListByRevisionAsync(
        Id<DecisionRevision> revisionId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<DecisionEvidenceReference>>(
                _items.Where(r => r.DecisionRevisionId == revisionId).OrderBy(r => r.Id).ToList());
        }
    }

    public Task<IReadOnlyDictionary<Id<DecisionRevision>, IReadOnlyList<DecisionEvidenceReference>>> ListByRevisionsAsync(
        IReadOnlyList<Id<DecisionRevision>> revisionIds, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var grouped = _items
                .Where(r => revisionIds.Contains(r.DecisionRevisionId))
                .GroupBy(r => r.DecisionRevisionId)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<DecisionEvidenceReference>)g.OrderBy(r => r.Id).ToList());
            return Task.FromResult<IReadOnlyDictionary<Id<DecisionRevision>, IReadOnlyList<DecisionEvidenceReference>>>(grouped);
        }
    }
}
