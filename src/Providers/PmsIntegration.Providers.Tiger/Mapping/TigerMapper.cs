using System.Text.Json;
using PmsIntegration.Core.Abstractions;
using PmsIntegration.Core.Contracts;

namespace PmsIntegration.Providers.Tiger.Mapping;

/// <summary>
/// Maps an <see cref="IntegrationJob"/> to the Tiger provider request body.
/// Pure mapping — no I/O, no side-effects.
/// </summary>
public sealed class TigerMapper : IPmsMapper
{
    public string Map(IntegrationJob job)
    {
        var payload = new
        {
            hotel_id       = job.HotelId,
            event_id       = job.EventId,
            event_type     = job.EventType,
            correlation_id = job.CorrelationId,
            payload        = job.Data
        };

        return JsonSerializer.Serialize(payload);
    }
}
