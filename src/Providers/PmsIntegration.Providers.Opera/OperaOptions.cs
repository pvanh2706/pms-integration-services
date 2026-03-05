namespace PmsIntegration.Providers.Opera;

/// <summary>Configuration for the Opera provider (appsettings Providers:OPERA section).</summary>
public sealed class OperaOptions
{
    public string BaseUrl { get; set; } = "https://api.opera-pms.example.com";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 20;
}
