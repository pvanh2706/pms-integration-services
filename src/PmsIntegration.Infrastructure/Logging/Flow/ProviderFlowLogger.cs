using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PmsIntegration.Core.Abstractions;

namespace PmsIntegration.Infrastructure.Logging.Flow;

/// <summary>
/// Stateful, scoped logger for the PROVIDER_FLOW document.
/// One instance is created per message-processing scope (Scoped DI lifetime).
///
/// Usage pattern:
///   1. Start(...)
///   2. Step(...) for each successful step
///   3. SetProviderRequest(...)
///   4. SetProviderResponse(...)
///   5. Fail(...) OR Complete()
///   6. Write()
/// </summary>
public sealed class ProviderFlowLogger : IProviderFlowLogger
{
    private readonly ILogger<ProviderFlowLogger> _logger;
    private readonly IClock _clock;
    private readonly IHostEnvironment _env;

    private readonly FlowLogDocument _doc;
    private bool _written;
    private bool _sealed;

    public ProviderFlowLogger(
        ILogger<ProviderFlowLogger> logger,
        IClock clock,
        IHostEnvironment env)
    {
        _logger = logger;
        _clock  = clock;
        _env    = env;

        _doc = new FlowLogDocument
        {
            LogType      = FlowLogType.ProviderFlow,
            Service      = "pms-integration-services",
            Environment  = env.EnvironmentName,
            TimestampUtc = clock.UtcNow,
            StartedAtUtc = clock.UtcNow
        };
    }

    /// <inheritdoc/>
    public void Start(
        string correlationId,
        string provider,
        string eventType,
        string? eventId   = null,
        string? hotelId   = null,
        string? site      = null,
        string? requestId = null,
        string? traceId   = null)
    {
        _doc.CorrelationId = correlationId;
        _doc.Provider      = provider;
        _doc.EventType     = eventType;
        _doc.EventId       = eventId;
        _doc.HotelId       = hotelId;
        _doc.Site          = site;
        _doc.RequestId     = requestId;
        _doc.TraceId       = traceId;
        _doc.StartedAtUtc  = _clock.UtcNow;
        _doc.TimestampUtc  = _doc.StartedAtUtc;
    }

    /// <inheritdoc/>
    public void Step(string stepName, DateTimeOffset startedAt, string? message = null)
    {
        if (_sealed) return;

        var now = _clock.UtcNow;
        _doc.Steps.Add(new FlowStepLog
        {
            Step         = stepName,
            Status       = FlowStatus.Success,
            StartedAtUtc = startedAt,
            EndedAtUtc   = now,
            Message      = message
        });
        _doc.CurrentStep = stepName;
    }

    /// <inheritdoc/>
    public void Fail(
        string stepName,
        DateTimeOffset startedAt,
        string errorCode,
        string errorMessage,
        string? message = null)
    {
        if (_sealed) return;
        _sealed = true;

        var now = _clock.UtcNow;
        _doc.Steps.Add(new FlowStepLog
        {
            Step         = stepName,
            Status       = FlowStatus.Failed,
            StartedAtUtc = startedAt,
            EndedAtUtc   = now,
            Message      = message ?? errorMessage
        });

        _doc.Status       = FlowStatus.Failed;
        _doc.FailedStep   = stepName;
        _doc.ErrorCode    = errorCode;
        _doc.ErrorMessage = errorMessage;
        _doc.CurrentStep  = stepName;
        _doc.EndedAtUtc   = now;
    }

    /// <inheritdoc/>
    public void Complete()
    {
        if (_sealed) return;
        _doc.EndedAtUtc = _clock.UtcNow;
        _doc.Status     = FlowStatus.Success;
    }

    /// <inheritdoc/>
    public void SetProviderRequest(
        string? transport,
        string? methodName,
        string? endpoint,
        string? contentType,
        string? maskedBody,
        long bodySizeBytes)
    {
        _doc.ProviderRequest = new ProviderRequestLog
        {
            Transport     = transport,
            MethodName    = methodName,
            Endpoint      = endpoint,
            ContentType   = contentType,
            BodyMasked    = maskedBody,
            BodySizeBytes = bodySizeBytes
        };
    }

    /// <inheritdoc/>
    public void SetProviderResponse(
        int? httpStatusCode,
        string? rawBody,
        string? maskedBody,
        long bodySizeBytes,
        string? parsedResult)
    {
        _doc.ProviderResponse = new ProviderResponseLog
        {
            HttpStatusCode = httpStatusCode,
            // Only store raw in Development to avoid leaking secrets
            BodyRaw        = _env.IsDevelopment() ? rawBody : null,
            BodyMasked     = maskedBody,
            BodySizeBytes  = bodySizeBytes,
            ParsedResult   = parsedResult
        };
    }

    /// <inheritdoc/>
    public void SetQueueBody(string? maskedBody) => _doc.QueueBodyMasked = maskedBody;

    /// <inheritdoc/>
    public void Write()
    {
        if (_written) return;
        _written = true;

        if (_doc.EndedAtUtc == default)
            _doc.EndedAtUtc = _clock.UtcNow;

        // Project only the fields needed for PROVIDER_FLOW documents.
        var slim = new
        {
            _doc.LogType,
            _doc.CorrelationId,
            _doc.EventType,
            _doc.StartedAtUtc,
            _doc.QueueBodyMasked,
            ProviderEndpoint     = _doc.ProviderRequest?.Endpoint,
            ProviderMethod       = _doc.ProviderRequest?.MethodName,
            ProviderRequestBody  = _doc.ProviderRequest?.BodyMasked,
            ProviderHttpStatus   = _doc.ProviderResponse?.HttpStatusCode,
            ProviderResponseBody = _doc.ProviderResponse?.BodyMasked,
            _doc.EndedAtUtc,
            _doc.DurationMs,
            _doc.Status,
            Steps = _doc.Steps.Select(s => new { s.Step, s.Status, s.DurationMs }).ToList(),
            // Fail fields — null on success, populated on failure
            _doc.FailedStep,
            _doc.ErrorCode,
            _doc.ErrorMessage,
        };

        _logger.LogInformation(
            "[{LogType}] corr={CorrelationId} provider={Provider} status={Status} steps={StepCount} duration={DurationMs}ms {@FlowLog}",
            _doc.LogType,
            _doc.CorrelationId,
            _doc.Provider,
            _doc.Status,
            _doc.Steps.Count,
            _doc.DurationMs,
            slim);
    }
}
