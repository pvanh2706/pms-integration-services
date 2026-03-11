using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PmsIntegration.Core.Abstractions;

namespace PmsIntegration.Infrastructure.Logging.Flow;

/// <summary>
/// Stateful, scoped logger for the API_FLOW document.
/// One instance is created per HTTP request (Scoped DI lifetime).
/// 
/// Usage pattern:
///   1. Start(...)
///   2. Step(...) for each successful processing step
///   3. SetRequestPayload(...)
///   4. Fail(...) OR Complete()
///   5. Write()
/// </summary>
public sealed class ApiFlowLogger : IApiFlowLogger
{
    private readonly ILogger<ApiFlowLogger> _logger;
    private readonly IClock _clock;
    private readonly IHostEnvironment _env;

    private readonly FlowLogDocument _doc;
    private bool _written;
    private bool _sealed; // once Fail is called, no more steps are accepted

    public ApiFlowLogger(
        ILogger<ApiFlowLogger> logger,
        IClock clock,
        IHostEnvironment env)
    {
        _logger = logger;
        _clock  = clock;
        _env    = env;

        _doc = new FlowLogDocument
        {
            LogType     = FlowLogType.ApiFlow,
            Service     = "pms-integration-services",
            Environment = env.EnvironmentName,
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
        if (_sealed) return; // flow already failed — ignore trailing steps

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
    public void SetRequestPayload(string? rawBody, string maskedBody, string? contentType)
    {
        var size = rawBody is not null
            ? System.Text.Encoding.UTF8.GetByteCount(rawBody)
            : System.Text.Encoding.UTF8.GetByteCount(maskedBody);

        _doc.RequestPayload = new PayloadLog
        {
            ContentType   = contentType,
            // Omit raw body in non-Development to avoid leaking secrets
            BodyRaw       = _env.IsDevelopment() ? rawBody : null,
            BodyMasked    = maskedBody,
            BodySizeBytes = size
        };
    }

    /// <inheritdoc/>
    public void SetHttpStatusCode(int statusCode) => _doc.HttpStatusCode = statusCode;

    /// <inheritdoc/>
    public void SetResponseBody(string responseBody) => _doc.ResponseBody = responseBody;

    /// <inheritdoc/>
    public void SetClientInfo(string? ipAddress, int? port, string? requestUrl)
    {
        _doc.ClientIpAddress = ipAddress;
        _doc.ClientPort      = port;
        _doc.RequestUrl      = requestUrl;
    }

    /// <inheritdoc/>
    public void Write()
    {
        if (_written) return;
        _written = true;

        // Ensure EndedAtUtc is set even if Complete/Fail was never called
        if (_doc.EndedAtUtc == default)
            _doc.EndedAtUtc = _clock.UtcNow;

        // Project only the fields needed for API_FLOW documents.
        // Null properties in the anonymous type will be omitted by Serilog destructuring.
        var slim = new
        {
            _doc.LogType,
            _doc.CorrelationId,
            _doc.StartedAtUtc,
            _doc.EndedAtUtc,
            _doc.DurationMs,
            _doc.Status,
            _doc.HttpStatusCode,
            _doc.ClientIpAddress,
            _doc.ClientPort,
            _doc.RequestUrl,
            RequestBody  = _doc.RequestPayload?.BodyMasked,
            ResponseBody = _doc.ResponseBody,
            Steps        = _doc.Steps.Select(s => new { s.Step, s.Status, s.DurationMs }).ToList(),
            // Fail fields — null on success, populated on failure
            _doc.FailedStep,
            _doc.ErrorCode,
            _doc.ErrorMessage,
        };

        _logger.LogInformation(
            "[{LogType}] corr={CorrelationId} status={Status} steps={StepCount} duration={DurationMs}ms {@FlowLog}",
            _doc.LogType,
            _doc.CorrelationId,
            _doc.Status,
            _doc.Steps.Count,
            _doc.DurationMs,
            slim);
    }
}
