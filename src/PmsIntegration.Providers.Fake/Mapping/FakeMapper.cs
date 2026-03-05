using System.Text.Json;
using PmsIntegration.Core.Abstractions;
using PmsIntegration.Core.Contracts;

namespace PmsIntegration.Providers.Fake.Mapping;

/// <summary>
/// Pure mapping function: IntegrationJob.Data → Fake provider schema.
/// </summary>
public sealed class FakeMapper : IPmsMapper
{
    public string Map(IntegrationJob job)
    {
        var payload = new
        {
            source = "pms-integration",
            hotelId = job.HotelId,
            eventId = job.EventId,
            eventType = job.EventType,
            correlationId = job.CorrelationId,
            data = job.Data
        };

        return JsonSerializer.Serialize(payload);
    }
}
