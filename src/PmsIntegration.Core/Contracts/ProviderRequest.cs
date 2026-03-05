namespace PmsIntegration.Core.Contracts;

/// <summary>
/// Provider-specific request built from an IntegrationJob.
/// </summary>
public sealed class ProviderRequest
{
    public string ProviderKey { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string Method { get; set; } = "POST";
    public string Endpoint { get; set; } = string.Empty;
    public string? JsonBody { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
}
