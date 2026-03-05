using System.Text.Json;
using PmsIntegration.Core.Abstractions;
using PmsIntegration.Core.Contracts;

namespace PmsIntegration.Providers.Opera.Mapping;

/// <summary>
/// Maps an <see cref="IntegrationJob"/> to the Opera Cloud provider request body.
/// Pure mapping — no I/O, no side-effects.
/// </summary>
public sealed class OperaMapper : IPmsMapper
{
    public string Map(IntegrationJob job)
    {
        var payload = new
        {
            HotelCode         = job.HotelId,
            ExternalReference = job.EventId,
            EventCode         = job.EventType,
            CorrelationId     = job.CorrelationId,
            Attributes        = job.Data
        };

        return JsonSerializer.Serialize(payload);
    }
}
