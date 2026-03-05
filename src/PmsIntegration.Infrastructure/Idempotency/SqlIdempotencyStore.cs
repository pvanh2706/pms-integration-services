using PmsIntegration.Core.Abstractions;

namespace PmsIntegration.Infrastructure.Idempotency;

/// <summary>
/// SQL-backed idempotency store (future implementation).
/// Placeholder skeleton — wire a DbContext or Dapper here.
/// </summary>
public sealed class SqlIdempotencyStore : IIdempotencyStore
{
    public Task<bool> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default) =>
        throw new NotImplementedException("SqlIdempotencyStore is not yet implemented.");

    public Task MarkSuccessAsync(string key, TimeSpan ttl, CancellationToken ct = default) =>
        throw new NotImplementedException("SqlIdempotencyStore is not yet implemented.");
}
