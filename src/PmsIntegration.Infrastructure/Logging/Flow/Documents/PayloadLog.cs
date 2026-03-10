namespace PmsIntegration.Infrastructure.Logging.Flow;

/// <summary>
/// Contains the masked/truncated API request body captured at the controller boundary.
/// </summary>
public sealed class PayloadLog
{
    public string? ContentType    { get; set; }

    /// <summary>Raw body string — intentionally null in production to avoid secret leakage.</summary>
    public string? BodyRaw        { get; set; }

    /// <summary>Sensitive fields replaced by "***". Always populated.</summary>
    public string? BodyMasked     { get; set; }

    public long BodySizeBytes     { get; set; }
}
