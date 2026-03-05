using System.Text.Json;

namespace PmsIntegration.Core.Contracts;

/// <summary>
/// Envelope received from the PMS via HTTP POST /api/pms/events.
/// </summary>
public sealed class PmsEventEnvelope
{
    public string HotelId { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public IReadOnlyList<string> Providers { get; set; } = Array.Empty<string>();
    public string? CorrelationId { get; set; }
    public JsonElement? Data { get; set; }
}
