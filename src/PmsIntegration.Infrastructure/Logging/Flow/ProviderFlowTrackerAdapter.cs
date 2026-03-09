using System.Text;
using PmsIntegration.Core.Abstractions;
using PmsIntegration.Core.Contracts;
using PmsIntegration.Infrastructure.Logging.Masking;

namespace PmsIntegration.Infrastructure.Logging.Flow;

/// <summary>
/// Bridges the Core-layer <see cref="IProviderFlowTracker"/> callbacks into
/// the Infrastructure <see cref="IProviderFlowLogger"/> calls.
///
/// This adapter is constructed by the consumer (Host) each time it starts a
/// PROVIDER_FLOW and is passed into <c>ProcessIntegrationJobHandler.HandleAsync</c>.
/// It keeps Application free of any Infrastructure reference.
/// </summary>
public sealed class ProviderFlowTrackerAdapter : IProviderFlowTracker
{
    private readonly IProviderFlowLogger _flowLogger;

    public ProviderFlowTrackerAdapter(IProviderFlowLogger flowLogger)
    {
        _flowLogger = flowLogger;
    }

    /// <inheritdoc/>
    public void OnStep(string stepName)
    {
        _flowLogger.Step(stepName, DateTimeOffset.UtcNow);
    }

    /// <inheritdoc/>
    public void OnRequestBuilt(ProviderRequest request)
    {
        _flowLogger.Step(ProviderFlowStep.RequestBuilt, DateTimeOffset.UtcNow);

        // Capture the outgoing provider request (body masked)
        var body      = request.JsonBody ?? string.Empty;
        var masked    = PayloadMasker.Mask(body);
        var sizeBytes = string.IsNullOrEmpty(body) ? 0L : Encoding.UTF8.GetByteCount(body);

        // Detect transport: Tiger SOAP requests use Content-Type text/xml
        var contentType = request.Headers.TryGetValue("Content-Type", out var ct) ? ct : "application/json";
        var transport   = contentType.Contains("xml", StringComparison.OrdinalIgnoreCase)
            ? "SOAP_HTTP"
            : "REST_HTTP";

        // Extract SOAP action / method name from the request body if possible
        string? methodName = null;
        if (transport == "SOAP_HTTP")
            methodName = ExtractSoapAction(body);

        _flowLogger.SetProviderRequest(
            transport     : transport,
            methodName    : methodName,
            endpoint      : request.Endpoint,
            contentType   : contentType,
            maskedBody    : masked,
            bodySizeBytes : sizeBytes);
    }

    /// <inheritdoc/>
    public void OnResponseReceived(ProviderResponse response)
    {
        _flowLogger.Step(ProviderFlowStep.HttpResponseReceived, DateTimeOffset.UtcNow);

        var rawBody   = response.Body ?? string.Empty;
        var masked    = PayloadMasker.Mask(rawBody);
        var sizeBytes = string.IsNullOrEmpty(rawBody) ? 0L : Encoding.UTF8.GetByteCount(rawBody);
        var parsed    = ParseTigerResult(rawBody, response.StatusCode);

        _flowLogger.SetProviderResponse(
            httpStatusCode : response.StatusCode,
            rawBody        : rawBody,
            maskedBody     : masked,
            bodySizeBytes  : sizeBytes,
            parsedResult   : parsed);

        _flowLogger.Step(ProviderFlowStep.ResponseParsed, DateTimeOffset.UtcNow,
            $"HTTP {response.StatusCode} — {parsed}");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the SOAP operation name from a SOAP Envelope body.
    /// Returns null if extraction fails (non-fatal).
    /// </summary>
    private static string? ExtractSoapAction(string soapBody)
    {
        // Look for the first element inside <soap:Body>
        // e.g. <checkIn xmlns="..."> → "checkIn"
        var bodyIdx = soapBody.IndexOf("<soap:Body>", StringComparison.OrdinalIgnoreCase);
        if (bodyIdx < 0) return null;

        var afterBody = soapBody.AsSpan(bodyIdx + 11).TrimStart();
        if (afterBody.IsEmpty || afterBody[0] != '<') return null;

        // Read up to first space or >
        var start = 1;
        var end   = start;
        while (end < afterBody.Length && afterBody[end] != ' ' && afterBody[end] != '>' && afterBody[end] != '/')
            end++;

        var element = afterBody.Slice(start, end - start).ToString();
        // Strip namespace prefix if present (e.g. "ns0:checkIn" → "checkIn")
        var colonIdx = element.IndexOf(':');
        return colonIdx >= 0 ? element[(colonIdx + 1)..] : element;
    }

    /// <summary>
    /// Parses a Tiger SOAP response body to produce a human-readable result string.
    /// Tiger returns success/failure inside the XML response body.
    /// </summary>
    private static string ParseTigerResult(string body, int statusCode)
    {
        if (statusCode < 200 || statusCode >= 300)
            return $"HTTP_ERROR_{statusCode}";

        if (string.IsNullOrWhiteSpace(body))
            return "EMPTY_RESPONSE";

        // Tiger success response typically contains "SUCCESS" or result="0"
        if (body.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase)
            || body.Contains("result=\"0\"", StringComparison.OrdinalIgnoreCase))
            return "SUCCESS";

        // Tiger fault / error response
        if (body.Contains("FAILED", StringComparison.OrdinalIgnoreCase)
            || body.Contains("Fault", StringComparison.OrdinalIgnoreCase)
            || body.Contains("result=\"1\"", StringComparison.OrdinalIgnoreCase))
            return "FAILED";

        return "UNKNOWN";
    }
}
