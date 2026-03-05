namespace PmsIntegration.Providers.Tiger;

/// <summary>Configuration for the Tiger provider (appsettings Providers:TIGER section).</summary>
public sealed class TigerOptions
{
    public string BaseUrl { get; set; } = "https://api.tiger-pms.example.com";
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 15;
}
