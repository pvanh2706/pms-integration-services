namespace PmsIntegration.Host.Options;

public sealed class PmsSecurityOptions
{
    public string FixedToken { get; set; } = string.Empty;
    public string HeaderName { get; set; } = "X-PMS-TOKEN";
}
