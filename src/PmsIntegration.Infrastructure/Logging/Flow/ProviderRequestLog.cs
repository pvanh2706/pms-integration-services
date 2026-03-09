namespace PmsIntegration.Infrastructure.Logging.Flow;

/// <summary>
/// Captures what was sent to the downstream provider (e.g. Tiger SOAP request).
/// </summary>
public sealed class ProviderRequestLog
{
    /// <summary>Transport protocol, e.g. "SOAP_HTTP", "REST_HTTP".</summary>
    public string? Transport    { get; set; }

    /// <summary>SOAP action / operation name, e.g. "checkIn".</summary>
    public string? MethodName   { get; set; }

    public string? Endpoint     { get; set; }
    public string? ContentType  { get; set; }

    /// <summary>Body with sensitive fields masked.</summary>
    public string? BodyMasked   { get; set; }

    public long BodySizeBytes   { get; set; }
}
