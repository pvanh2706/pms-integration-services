namespace PmsIntegration.Host.Options;

public sealed class PmsSecurityOptions
{
    public string FixedToken { get; set; } = string.Empty;
    public string HeaderName { get; set; } = "X-PMS-TOKEN";

    /// <summary>
    /// Maximum age in seconds that a request timestamp (X-PMS-TIMESTAMP) is considered valid.
    /// Set to 0 to disable replay-window enforcement (not recommended for production).
    /// Default: 300 (5 minutes).
    /// </summary>
    public int ReplayWindowSeconds { get; set; } = 300;
}
