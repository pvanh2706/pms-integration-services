namespace PmsIntegration.Infrastructure.Logging.Flow;

/// <summary>
/// Common contract for ApiFlowLogger and ProviderFlowLogger.
/// Implementations are stateful: they accumulate steps and payload data during
/// request/message processing, then write a single document at the end.
/// 
/// Lifetime: Scoped (one instance per HTTP request or per message-processing scope).
/// </summary>
public interface IFlowLogger
{
    /// <summary>
    /// Initialises the flow document with identity/context fields.
    /// Must be called once at the start of processing.
    /// </summary>
    void Start(
        string correlationId,
        string provider,
        string eventType,
        string? eventId    = null,
        string? hotelId    = null,
        string? site       = null,
        string? requestId  = null,
        string? traceId    = null);

    /// <summary>
    /// Records a successfully completed step (Status = SUCCESS).
    /// </summary>
    void Step(string stepName, DateTimeOffset startedAt, string? message = null);

    /// <summary>
    /// Records a failed step and marks the overall flow as FAILED.
    /// Subsequent calls to Step/Complete are no-ops — the flow is sealed.
    /// </summary>
    void Fail(
        string stepName,
        DateTimeOffset startedAt,
        string errorCode,
        string errorMessage,
        string? message = null);

    /// <summary>
    /// Marks the flow as successfully completed and sets the final EndedAtUtc.
    /// </summary>
    void Complete();

    /// <summary>
    /// Writes the accumulated document to the structured logger.
    /// Safe to call multiple times; only the first call writes.
    /// </summary>
    void Write();
}
