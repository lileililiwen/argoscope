using System.Collections.Concurrent;
using Argoscope.Domain.Common;
using Argoscope.Domain.Identity;

namespace Argoscope.Application.Identity;

/// <summary>Thread-safe in-memory bounded session store for the API layer.</summary>
public sealed class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new();
    private readonly IClock _clock;
    private readonly TimeSpan _lifetime;

    public InMemorySessionStore(IClock clock, TimeSpan lifetime)
    {
        _clock = clock;
        _lifetime = lifetime;
    }

    public Task<SessionEntry> IssueAsync(Id<Tenant> tenantId, string subject, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var token = Convert.ToBase64String(Guid.NewGuid().ToByteArray())
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var entry = new SessionEntry(token, tenantId, subject, now.Add(_lifetime));
        _sessions[token] = entry;
        return Task.FromResult(entry);
    }

    public Task<SessionEntry?> FindAsync(string token, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(token) || !_sessions.TryGetValue(token, out var entry))
        {
            return Task.FromResult<SessionEntry?>(null);
        }

        if (now > entry.ExpiresAtUtc)
        {
            _sessions.TryRemove(token, out _);
            return Task.FromResult<SessionEntry?>(null);
        }

        return Task.FromResult<SessionEntry?>(entry);
    }

    public Task RevokeAsync(string token, CancellationToken cancellationToken)
    {
        _sessions.TryRemove(token, out _);
        return Task.CompletedTask;
    }
}
