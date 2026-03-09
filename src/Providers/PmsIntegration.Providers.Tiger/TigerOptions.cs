namespace PmsIntegration.Providers.Tiger;

/// <summary>Configuration for the Tiger provider (appsettings Providers:TIGER section).</summary>
public sealed class TigerOptions
{
    public string BaseUrl { get; set; } = "https://api.tiger-pms.example.com";
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>SOAP endpoint for TigerTMS GenericPMS interface.</summary>
    public string SoapEndpoint { get; set; } = "https://ichargev7publictest.tigertms.com/GenericPMS/";

    /// <summary>Web-service user key passed inside the SOAP CheckIn payload (wsuserkey).</summary>
    public string WsUserKey { get; set; } = string.Empty;
}
