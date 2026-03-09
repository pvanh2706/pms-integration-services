namespace PmsIntegration.Infrastructure.Logging.Flow;

/// <summary>
/// Extension of IFlowLogger for the provider/consumer side.
/// Adds methods to attach the Tiger SOAP request and response payloads.
/// </summary>
public interface IProviderFlowLogger : IFlowLogger
{
    /// <summary>
    /// Records the outgoing SOAP/HTTP request sent to Tiger.
    /// Call after building the request, before sending.
    /// </summary>
    void SetProviderRequest(
        string? transport,
        string? methodName,
        string? endpoint,
        string? contentType,
        string? maskedBody,
        long bodySizeBytes);

    /// <summary>
    /// Records the HTTP response received from Tiger.
    /// Call immediately after receiving, before parsing.
    /// </summary>
    void SetProviderResponse(
        int? httpStatusCode,
        string? rawBody,
        string? maskedBody,
        long bodySizeBytes,
        string? parsedResult);
}
