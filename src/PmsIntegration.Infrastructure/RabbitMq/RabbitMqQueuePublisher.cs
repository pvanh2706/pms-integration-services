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
        await using var channel = await conn.CreateChannelAsync(cancellationToken: ct);

        // BUG-5 FIX: enable publisher confirms so a broker-side nack/timeout surfaces
        // as an exception here rather than silently losing the retry/DLQ message.
        await channel.ConfirmSelectAsync(trackConfirmations: false, cancellationToken: ct);

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

        // Throws AmqpException if the broker nacks or the channel is closed.
        await channel.WaitForConfirmsOrDieAsync(ct);
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
