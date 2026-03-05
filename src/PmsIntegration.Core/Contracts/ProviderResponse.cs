namespace PmsIntegration.Core.Contracts;

/// <summary>
/// Raw HTTP response from the provider API.
/// </summary>
public sealed class ProviderResponse
{
    public int StatusCode { get; set; }
    public string? Body { get; set; }
    public bool IsSuccess => StatusCode is >= 200 and < 300;
}
