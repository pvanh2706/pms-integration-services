namespace PmsIntegration.Core.Abstractions;

/// <summary>
/// Guards against duplicate provider API calls.
/// Key format: hotelId:eventId:eventType:providerKey
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Attempts to acquire a processing lease.
    /// Returns false if a record already exists (duplicate).
    /// </summary>
    Task<bool> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>
    /// Extends the TTL on success for longer retention.
    /// </summary>
    Task MarkSuccessAsync(string key, TimeSpan ttl, CancellationToken ct = default);
}
