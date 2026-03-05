using PmsIntegration.Core.Contracts;

namespace PmsIntegration.Core.Abstractions;

/// <summary>
/// Publishes integration jobs to a provider queue.
/// </summary>
public interface IQueuePublisher
{
    Task PublishAsync(IntegrationJob job, string queue, CancellationToken ct = default);
    Task PublishRetryAsync(IntegrationJob job, string retryQueue, int attempt, string? errorCode, string? errorMessage, CancellationToken ct = default);
    Task PublishDlqAsync(IntegrationJob job, string dlqQueue, int attempt, string? errorCode, string? errorMessage, CancellationToken ct = default);
}
