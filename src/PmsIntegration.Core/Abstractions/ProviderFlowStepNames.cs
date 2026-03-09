namespace PmsIntegration.Core.Abstractions;

/// <summary>
/// Generic step name constants used when calling <see cref="IProviderFlowTracker.OnStep"/>.
/// Provider-agnostic — works for Tiger, Opera, and any future provider.
/// Kept in Core so Application can use them without an Infrastructure reference.
/// </summary>
public static class ProviderFlowStepNames
{
    public const string ProviderPayloadValidated = "PROVIDER_PAYLOAD_VALIDATED";
    public const string RequestBuilding          = "REQUEST_BUILDING";
    public const string RequestBuilt             = "REQUEST_BUILT";
    public const string HttpSending              = "HTTP_SENDING";
    public const string HttpResponseReceived     = "HTTP_RESPONSE_RECEIVED";
    public const string ResponseParsed           = "RESPONSE_PARSED";
}
