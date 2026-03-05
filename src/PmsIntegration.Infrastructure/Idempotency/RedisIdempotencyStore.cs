using PmsIntegration.Core.Abstractions;
using StackExchange.Redis;

namespace PmsIntegration.Infrastructure.Idempotency;

/// <summary>
/// Production idempotency store backed by Redis.
/// Uses SET NX EX for atomic acquire.
/// </summary>
public sealed class RedisIdempotencyStore : IIdempotencyStore
{
    private readonly IConnectionMultiplexer _redis;

    public RedisIdempotencyStore(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<bool> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        return await db.StringSetAsync(key, "1", ttl, When.NotExists);
    }

    public async Task MarkSuccessAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        await db.KeyExpireAsync(key, ttl);
    }
}
