using PmsIntegration.Core.Contracts;

namespace PmsIntegration.Application.Services;

/// <summary>
/// Validates incoming PMS event envelopes.
/// Throws ArgumentException for non-retryable validation failures.
/// </summary>
public sealed class EventValidator
{
    public void Validate(PmsEventEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope.HotelId))
            throw new ArgumentException("HotelId is required.", nameof(envelope));

        if (string.IsNullOrWhiteSpace(envelope.EventId))
            throw new ArgumentException("EventId is required.", nameof(envelope));

        if (string.IsNullOrWhiteSpace(envelope.EventType))
            throw new ArgumentException("EventType is required.", nameof(envelope));

        if (envelope.Providers is null || envelope.Providers.Count == 0)
            throw new ArgumentException("At least one provider must be specified.", nameof(envelope));
    }
}
