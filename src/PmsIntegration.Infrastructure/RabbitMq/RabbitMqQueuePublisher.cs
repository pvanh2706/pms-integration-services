using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using PmsIntegration.Core.Abstractions;
using PmsIntegration.Core.Contracts;

namespace PmsIntegration.Infrastructure.RabbitMq;

public sealed class RabbitMqQueuePublisher : IQueuePublisher
{
    private readonly RabbitMqConnectionFactory _factory;

    public RabbitMqQueuePublisher(RabbitMqConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task PublishAsync(IntegrationJob job, string queue, CancellationToken ct = default)
    {
        var headers = BuildHeaders(job, attempt: 1);
        await PublishCoreAsync(job, queue, headers, ct);
    }

    public async Task PublishRetryAsync(IntegrationJob job, string retryQueue, int attempt,
        string? errorCode, string? errorMessage, CancellationToken ct = default)
    {
        var headers = BuildHeaders(job, attempt + 1);
        headers[RabbitMqHeaders.LastErrorCode] = errorCode ?? string.Empty;
        headers[RabbitMqHeaders.LastErrorMessage] = errorMessage ?? string.Empty;
        await PublishCoreAsync(job, retryQueue, headers, ct);
    }

    public async Task PublishDlqAsync(IntegrationJob job, string dlqQueue, int attempt,
        string? errorCode, string? errorMessage, CancellationToken ct = default)
    {
        var headers = BuildHeaders(job, attempt);
        headers[RabbitMqHeaders.LastErrorCode] = errorCode ?? string.Empty;
        headers[RabbitMqHeaders.LastErrorMessage] = errorMessage ?? string.Empty;
        await PublishCoreAsync(job, dlqQueue, headers, ct);
    }

    private async Task PublishCoreAsync(IntegrationJob job, string queue,
        Dictionary<string, object?> headers, CancellationToken ct)
    {
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(job));
        var conn = await _factory.GetConnectionAsync(ct);

        // Publisher confirms in RabbitMQ.Client v7+: enabled via CreateChannelOptions.
        // ConfirmSelectAsync / WaitForConfirmsOrDieAsync were removed in v7.
        // With PublisherConfirmationTrackingEnabled = true, BasicPublishAsync awaits
        // the broker ack internally — a nack surfaces as PublisherConfirmationException.
        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled:        true,
            publisherConfirmationTrackingEnabled: true);
        await using var channel = await conn.CreateChannelAsync(channelOptions, ct);

        var props = new BasicProperties
        {
            Persistent = true,
            Headers = headers,
            CorrelationId = job.CorrelationId,
            MessageId = job.JobId
        };

        await channel.BasicPublishAsync(
            exchange: "",
            routingKey: queue,
            mandatory: true,
            basicProperties: props,
            body: body,
            cancellationToken: ct);
        // With PublisherConfirmationTrackingEnabled = true, BasicPublishAsync already
        // awaits the broker ack. No separate WaitForConfirmsOrDieAsync needed.
    }

    private static Dictionary<string, object?> BuildHeaders(IntegrationJob job, int attempt) =>
        new()
        {
            [RabbitMqHeaders.CorrelationId] = job.CorrelationId,
            [RabbitMqHeaders.HotelId] = job.HotelId,
            [RabbitMqHeaders.EventId] = job.EventId,
            [RabbitMqHeaders.EventType] = job.EventType,
            [RabbitMqHeaders.ProviderKey] = job.ProviderKey,
            [RabbitMqHeaders.RetryAttempt] = attempt
        };
}
