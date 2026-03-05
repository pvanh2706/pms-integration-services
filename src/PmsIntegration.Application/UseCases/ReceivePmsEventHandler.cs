using Microsoft.Extensions.Logging;
using PmsIntegration.Application.Services;
using PmsIntegration.Core.Abstractions;
using PmsIntegration.Core.Contracts;

namespace PmsIntegration.Application.UseCases;

/// <summary>
/// Orchestrates the inbound PMS event flow:
///   validate → fan-out → enqueue per provider.
/// </summary>
public sealed class ReceivePmsEventHandler
{
    private readonly EventValidator _validator;
    private readonly ProviderRouter _router;
    private readonly IQueuePublisher _publisher;
    private readonly IConfigProvider _config;
    private readonly IAuditLogger _audit;
    private readonly IClock _clock;
    private readonly ILogger<ReceivePmsEventHandler> _logger;

    public ReceivePmsEventHandler(
        EventValidator validator,
        ProviderRouter router,
        IQueuePublisher publisher,
        IConfigProvider config,
        IAuditLogger audit,
        IClock clock,
        ILogger<ReceivePmsEventHandler> logger)
    {
        _validator = validator;
        _router = router;
        _publisher = publisher;
        _config = config;
        _audit = audit;
        _clock = clock;
        _logger = logger;
    }

    public async Task<string> HandleAsync(PmsEventEnvelope envelope, CancellationToken ct = default)
    {
        var correlationId = string.IsNullOrWhiteSpace(envelope.CorrelationId)
            ? Guid.NewGuid().ToString()
            : envelope.CorrelationId;

        _audit.Log("pms.received", new
        {
            correlationId,
            hotelId = envelope.HotelId,
            eventId = envelope.EventId,
            eventType = envelope.EventType
        });

        _validator.Validate(envelope);

        var jobs = envelope.Providers
            .Select(p => new IntegrationJob
            {
                JobId = Guid.NewGuid().ToString(),
                HotelId = envelope.HotelId,
                EventId = envelope.EventId,
                EventType = envelope.EventType,
                ProviderKey = p.Trim().ToUpperInvariant(),
                CorrelationId = correlationId,
                CreatedAtUtc = _clock.UtcNow,
                Data = envelope.Data
            })
            .ToList();

        foreach (var job in jobs)
        {
            var queue = _router.ResolveQueue(job.ProviderKey);

            await _publisher.PublishAsync(job, queue, ct);

            _audit.Log("job.enqueued", new
            {
                correlationId,
                hotelId = job.HotelId,
                eventId = job.EventId,
                eventType = job.EventType,
                providerKey = job.ProviderKey,
                jobId = job.JobId,
                queue
            });
        }

        return correlationId;
    }
}
