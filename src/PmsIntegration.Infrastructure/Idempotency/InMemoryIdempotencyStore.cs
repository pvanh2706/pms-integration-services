using System.Collections.Concurrent;
using PmsIntegration.Core.Abstractions;

namespace PmsIntegration.Infrastructure.Idempotency;

/// <summary>
/// In-memory idempotency store backed by a ConcurrentDictionary with expiry.
/// Default implementation for development / testing.
/// </summary>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    // value = expiry UTC
    private readonly ConcurrentDictionary<string, DateTimeOffset> _store = new();

    public Task<bool> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        // Purge expired entries opportunistically
        foreach (var expired in _store.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList())
            _store.TryRemove(expired, out _);

        var acquired = _store.TryAdd(key, now + ttl);
        return Task.FromResult(acquired);
    }

    public Task MarkSuccessAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        _store[key] = DateTimeOffset.UtcNow + ttl;
        return Task.CompletedTask;
    }
}
