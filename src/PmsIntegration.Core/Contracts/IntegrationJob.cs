using System.Text.Json;

namespace PmsIntegration.Core.Contracts;

/// <summary>
/// A unit of work dispatched to a provider queue.
/// Idempotency key: hotelId:eventId:eventType:providerKey
/// </summary>
public sealed class IntegrationJob
{
    public string JobId { get; set; } = Guid.NewGuid().ToString();
    public string HotelId { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string ProviderKey { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public JsonElement? Data { get; set; }

    /// <summary>
    /// Preserved raw message body for poison messages that could not be deserialized.
    /// Populated only when <see cref="EventType"/> is <c>"POISON"</c>.
    /// </summary>
    public string? RawPayload { get; set; }
}
