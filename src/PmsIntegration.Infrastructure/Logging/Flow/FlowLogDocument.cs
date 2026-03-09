namespace PmsIntegration.Infrastructure.Logging.Flow;

/// <summary>
/// The single Elasticsearch document written per flow (API_FLOW or PROVIDER_FLOW).
/// All fields at the root level are indexed; nested objects (Payload, ProviderRequest,
/// ProviderResponse, Steps) are stored as nested/object mappings.
/// </summary>
public sealed class FlowLogDocument
{
    // ── Elasticsearch standard fields ──────────────────────────────────────

    /// <summary>ISO-8601 UTC; Kibana uses "@timestamp" by default — mapped via Serilog property name.</summary>
    public DateTimeOffset TimestampUtc { get; set; }

    // ── Classification ─────────────────────────────────────────────────────

    /// <summary>"API_FLOW" or "PROVIDER_FLOW". See <see cref="FlowLogType"/>.</summary>
    public string LogType    { get; set; } = string.Empty;

    public string Service    { get; set; } = "pms-integration-services";
    public string Environment { get; set; } = string.Empty;

    // ── Tracing ────────────────────────────────────────────────────────────

    /// <summary>Links API_FLOW and PROVIDER_FLOW for the same business event.</summary>
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>HTTP request ID set by ASP.NET Core TraceIdentifier (API side).</summary>
    public string? RequestId   { get; set; }

    /// <summary>OpenTelemetry / W3C trace ID if available.</summary>
    public string? TraceId     { get; set; }

    // ── Business context ───────────────────────────────────────────────────

    public string? HotelId    { get; set; }
    public string? Site       { get; set; }
    public string Provider    { get; set; } = string.Empty;
    public string EventType   { get; set; } = string.Empty;
    public string? EventId    { get; set; }

    // ── Outcome ────────────────────────────────────────────────────────────

    /// <summary>"SUCCESS" | "FAILED". See <see cref="FlowStatus"/>.</summary>
    public string Status       { get; set; } = FlowStatus.Success;

    /// <summary>Last successfully executed step when Status==SUCCESS.</summary>
    public string CurrentStep  { get; set; } = string.Empty;

    /// <summary>Step where execution broke down (null on success).</summary>
    public string? FailedStep  { get; set; }

    public string? ErrorCode   { get; set; }
    public string? ErrorMessage { get; set; }

    // ── Timing ─────────────────────────────────────────────────────────────

    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset EndedAtUtc   { get; set; }
    public long DurationMs             => (long)(EndedAtUtc - StartedAtUtc).TotalMilliseconds;

    // ── Step detail ────────────────────────────────────────────────────────

    public List<FlowStepLog> Steps { get; set; } = [];

    // ── Payload detail (API_FLOW only) ─────────────────────────────────────

    public PayloadLog? RequestPayload { get; set; }

    // ── Provider I/O (PROVIDER_FLOW only) ─────────────────────────────────

    public ProviderRequestLog?  ProviderRequest  { get; set; }
    public ProviderResponseLog? ProviderResponse { get; set; }
}
