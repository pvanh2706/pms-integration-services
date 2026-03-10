namespace PmsIntegration.Infrastructure.Logging.Flow;

/// <summary>
/// Captures the raw + parsed result of the downstream provider response.
/// </summary>
public sealed class ProviderResponseLog
{
    public int? HttpStatusCode  { get; set; }

    /// <summary>Raw response body — may be kept null in production.</summary>
    public string? BodyRaw      { get; set; }

    /// <summary>Sensitive fields replaced by "***".</summary>
    public string? BodyMasked   { get; set; }

    public long BodySizeBytes   { get; set; }

    /// <summary>Human-readable parse result, e.g. "SUCCESS", "FAILED", "PARSE_ERROR".</summary>
    public string? ParsedResult { get; set; }
}
