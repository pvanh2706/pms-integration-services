using PmsIntegration.Core.Domain;

namespace PmsIntegration.Core.Contracts;

/// <summary>
/// Final result returned by the consumer after processing a job.
/// </summary>
public sealed class IntegrationResult
{
    public IntegrationOutcome Outcome { get; private init; }
    public string? ErrorCode { get; private init; }
    public string? ErrorMessage { get; private init; }
    public int? HttpStatusCode { get; private init; }

    private IntegrationResult() { }

    public static IntegrationResult Succeeded() =>
        new() { Outcome = IntegrationOutcome.Success };

    public static IntegrationResult RetryableFailed(string errorCode, string message, int? statusCode = null) =>
        new()
        {
            Outcome = IntegrationOutcome.RetryableFailure,
            ErrorCode = errorCode,
            ErrorMessage = message,
            HttpStatusCode = statusCode
        };

    public static IntegrationResult NonRetryableFailed(string errorCode, string message, int? statusCode = null) =>
        new()
        {
            Outcome = IntegrationOutcome.NonRetryableFailure,
            ErrorCode = errorCode,
            ErrorMessage = message,
            HttpStatusCode = statusCode
        };
}
