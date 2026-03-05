using System.Diagnostics;

namespace PmsIntegration.Infrastructure.Http.DelegatingHandlers;

/// <summary>
/// Injects X-Correlation-Id header into every outbound provider HTTP request.
/// </summary>
public sealed class CorrelationIdHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        if (!request.Headers.Contains("X-Correlation-Id"))
        {
            var correlationId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString();
            request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
        }
        return base.SendAsync(request, ct);
    }
}
