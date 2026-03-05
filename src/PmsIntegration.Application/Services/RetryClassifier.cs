using PmsIntegration.Core.Contracts;

namespace PmsIntegration.Application.Services;

/// <summary>
/// Classifies HTTP status codes and exceptions into retry vs non-retry outcomes.
/// Per CONVENTIONS.md §8.
/// </summary>
public sealed class RetryClassifier
{
    private static readonly HashSet<int> RetryableStatusCodes = [408, 429];

    public IntegrationResult ClassifyHttpStatus(int statusCode)
    {
        // 2xx — should not reach here; caller should short-circuit
        if (statusCode is >= 200 and < 300)
            return IntegrationResult.Succeeded();

        // Retryable: 408, 429, 5xx
        if (RetryableStatusCodes.Contains(statusCode) || statusCode >= 500)
            return IntegrationResult.RetryableFailed(
                $"HTTP_{statusCode}",
                $"Received retryable HTTP status {statusCode}.",
                statusCode);

        // Non-retryable: remaining 4xx
        return IntegrationResult.NonRetryableFailed(
            $"HTTP_{statusCode}",
            $"Received non-retryable HTTP status {statusCode}.",
            statusCode);
    }

    public IntegrationResult ClassifyException(Exception ex)
    {
        return ex switch
        {
            TaskCanceledException or OperationCanceledException or TimeoutException =>
                IntegrationResult.RetryableFailed("TIMEOUT", ex.Message),

            HttpRequestException httpEx =>
                IntegrationResult.RetryableFailed("HTTP_REQUEST", httpEx.Message),

            ArgumentException or InvalidOperationException or NotSupportedException =>
                IntegrationResult.NonRetryableFailed("MAPPING_ERROR", ex.Message),

            _ => IntegrationResult.RetryableFailed("UNKNOWN", ex.Message)
        };
    }
}
