namespace PmsIntegration.Infrastructure.Logging.Flow;

/// <summary>
/// Well-known status strings shared by FlowLogDocument and FlowStepLog.
/// Using constants avoids magic strings scattered across the codebase.
/// </summary>
public static class FlowStatus
{
    public const string Success = "SUCCESS";
    public const string Failed  = "FAILED";
    public const string Skipped = "SKIPPED";
}

/// <summary>
/// Well-known LogType values that identify which of the 2 flow documents
/// a given Elasticsearch document represents.
/// </summary>
public static class FlowLogType
{
    public const string ApiFlow      = "API_FLOW";
    public const string ProviderFlow = "PROVIDER_FLOW";
}

/// <summary>
/// Step names for the API_FLOW document (controller → queue publish).
/// </summary>
public static class ApiFlowStep
{
    public const string RequestReceived    = "REQUEST_RECEIVED";
    public const string RequestDeserialized = "REQUEST_DESERIALIZED";
    public const string RequestValidated   = "REQUEST_VALIDATED";
    public const string ProviderResolved   = "PROVIDER_RESOLVED";
    public const string QueueNameResolved  = "QUEUE_NAME_RESOLVED";
    public const string MessageMapped      = "MESSAGE_MAPPED";
    public const string QueuePublishing    = "QUEUE_PUBLISHING";
    public const string QueuePublished     = "QUEUE_PUBLISHED";
    public const string ApiResponseReady   = "API_RESPONSE_READY";
}

/// <summary>
/// Step names for the PROVIDER_FLOW document (consumer → provider → ack).
/// Generic — not tied to any specific provider.
/// </summary>
public static class ProviderFlowStep
{
    public const string MessageReceived          = "MESSAGE_RECEIVED";
    public const string MessageDeserialized      = "MESSAGE_DESERIALIZED";
    public const string ProviderPayloadValidated = "PROVIDER_PAYLOAD_VALIDATED";
    public const string RequestBuilding          = "REQUEST_BUILDING";
    public const string RequestBuilt             = "REQUEST_BUILT";
    public const string HttpSending              = "HTTP_SENDING";
    public const string HttpResponseReceived     = "HTTP_RESPONSE_RECEIVED";
    public const string ResponseParsed           = "RESPONSE_PARSED";
    public const string MessageAcked             = "MESSAGE_ACKED";
    public const string MessageNacked            = "MESSAGE_NACKED";
    public const string RetryScheduled           = "RETRY_SCHEDULED";
}
