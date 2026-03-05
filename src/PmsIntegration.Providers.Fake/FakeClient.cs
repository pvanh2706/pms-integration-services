using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PmsIntegration.Core.Abstractions;
using PmsIntegration.Core.Contracts;

namespace PmsIntegration.Providers.Fake;

/// <summary>
/// Fake provider client. Simulates an HTTP call without real network I/O.
/// Controlled via FakeOptions.SimulateFailure / SimulatedStatusCode.
/// </summary>
public sealed class FakeClient : IPmsClient
{
    public string ProviderKey => "FAKE";

    private readonly FakeOptions _options;
    private readonly ILogger<FakeClient> _logger;

    public FakeClient(IOptions<FakeOptions> options, ILogger<FakeClient> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<ProviderResponse> SendAsync(ProviderRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("[FAKE] Sending {Method} {Endpoint} correlationId={CorrelationId}",
            request.Method, request.Endpoint, request.CorrelationId);

        if (_options.SimulateFailure)
        {
            _logger.LogWarning("[FAKE] Simulating failure with status {Status}", _options.SimulatedStatusCode);
            return Task.FromResult(new ProviderResponse
            {
                StatusCode = _options.SimulatedStatusCode,
                Body = $"{{\"error\":\"simulated failure\"}}"
            });
        }

        _logger.LogInformation("[FAKE] Success response for correlationId={CorrelationId}", request.CorrelationId);
        return Task.FromResult(new ProviderResponse { StatusCode = 200, Body = "{\"status\":\"accepted\"}" });
    }
}
