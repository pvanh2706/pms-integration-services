using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PmsIntegration.Core.Abstractions;
using PmsIntegration.Core.Contracts;

namespace PmsIntegration.Providers.Opera;

/// <summary>
/// HTTP client for the Opera Cloud provider.
/// Uses OAuth 2.0 client-credentials flow (placeholder — wire token fetch when available).
/// </summary>
public sealed class OperaClient : IPmsClient
{
    public string ProviderKey => "OPERA";

    private readonly IHttpClientFactory _httpFactory;
    private readonly OperaOptions _options;
    private readonly ILogger<OperaClient> _logger;

    public OperaClient(
        IHttpClientFactory httpFactory,
        IOptions<OperaOptions> options,
        ILogger<OperaClient> logger)
    {
        _httpFactory = httpFactory;
        _options     = options.Value;
        _logger      = logger;
    }

    public async Task<ProviderResponse> SendAsync(ProviderRequest request, CancellationToken ct = default)
    {
        var http = _httpFactory.CreateClient("OPERA");

        using var httpRequest = new HttpRequestMessage(
            new HttpMethod(request.Method),
            request.Endpoint);

        if (request.JsonBody is not null)
            httpRequest.Content = new StringContent(request.JsonBody, Encoding.UTF8, "application/json");

        // TODO: obtain Bearer token via client-credentials and attach here
        foreach (var (key, value) in request.Headers)
            httpRequest.Headers.TryAddWithoutValidation(key, value);

        _logger.LogDebug("[OPERA] {Method} {Endpoint}", request.Method, request.Endpoint);

        var response = await http.SendAsync(httpRequest, ct);
        var body     = await response.Content.ReadAsStringAsync(ct);

        return new ProviderResponse { StatusCode = (int)response.StatusCode, Body = body };
    }
}
