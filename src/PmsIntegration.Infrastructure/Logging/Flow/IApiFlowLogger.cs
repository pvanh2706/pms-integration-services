namespace PmsIntegration.Infrastructure.Logging.Flow;

/// <summary>
/// Extension of IFlowLogger for the API side.
/// Adds the ability to attach the raw/masked request body received at the controller.
/// </summary>
public interface IApiFlowLogger : IFlowLogger
{
    /// <summary>
    /// Captures the API request body (raw + masked) for the API_FLOW document.
    /// Typically called right after the controller reads the body.
    /// </summary>
    /// <param name="rawBody">
    /// Full request body as received. Pass <c>null</c> in production if you
    /// don't want raw payloads logged.
    /// </param>
    /// <param name="maskedBody">Body with sensitive fields replaced by "***".</param>
    /// <param name="contentType">e.g. "application/json".</param>
    void SetRequestPayload(string? rawBody, string maskedBody, string? contentType);
}
