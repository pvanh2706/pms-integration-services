using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PmsIntegration.Core.Abstractions;
using PmsIntegration.Core.Contracts;

namespace PmsIntegration.Providers.Tiger;

/// <summary>
/// HTTP client for the Tiger provider.
/// Authenticates via API-key header (HMAC — wire real auth when available).
/// </summary>
public sealed class TigerClient : IPmsClient
{
    public string ProviderKey => "TIGER";

    private readonly IHttpClientFactory _httpFactory;
    private readonly TigerOptions _options;
    private readonly ILogger<TigerClient> _logger;

    public TigerClient(
        IHttpClientFactory httpFactory,
        IOptions<TigerOptions> options,
        ILogger<TigerClient> logger)
    {
        _httpFactory = httpFactory;
        _options     = options.Value;
        _logger      = logger;
    }

    public async Task<ProviderResponse> SendAsync(ProviderRequest request, CancellationToken ct = default)
    {
        var http = _httpFactory.CreateClient("TIGER");

        using var httpRequest = new HttpRequestMessage(
            new HttpMethod(request.Method),
            request.Endpoint);

        if (request.JsonBody is not null)
            httpRequest.Content = new StringContent(request.JsonBody, Encoding.UTF8, "application/json");

        foreach (var (key, value) in request.Headers)
            httpRequest.Headers.TryAddWithoutValidation(key, value);

        _logger.LogDebug("[TIGER] {Method} {Endpoint}", request.Method, request.Endpoint);

        var response = await http.SendAsync(httpRequest, ct);
        var body     = await response.Content.ReadAsStringAsync(ct);

        return new ProviderResponse { StatusCode = (int)response.StatusCode, Body = body };
    }
}
