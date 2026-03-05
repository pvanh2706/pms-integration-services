using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using PmsIntegration.Application.Services;
using PmsIntegration.Application.UseCases;
using PmsIntegration.Core.Contracts;
using PmsIntegration.Core.Abstractions;
using PmsIntegration.Core.Domain;
using PmsIntegration.Infrastructure.Options;
using PmsIntegration.Infrastructure.RabbitMq;

namespace PmsIntegration.Host.Background;

/// <summary>
/// Background service that subscribes to a provider's main queue,
/// processes jobs, and applies ACK/retry/DLQ logic per CONVENTIONS.md §7.
/// Includes a reconnect loop: when the channel closes due to broker disconnect
/// (after auto-recovery exhaustion) the service re-acquires connection + channel
/// automatically rather than hanging forever.
/// </summary>
public sealed class ProviderConsumerService : BackgroundService
{
    private readonly string _mainQueue;
    private readonly string _retryQueue;
    private readonly string _dlqQueue;
    private readonly RabbitMqConnectionFactory _connectionFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IQueuePublisher _publisher;
    private readonly QueueOptions _queueOptions;
    private readonly int _reconnectDelaySeconds;
    private readonly ILogger<ProviderConsumerService> _logger;

    public ProviderConsumerService(
        string mainQueue,
        RabbitMqConnectionFactory connectionFactory,
        IServiceScopeFactory scopeFactory,
        IQueuePublisher publisher,
        IOptions<QueueOptions> queueOptions,
        IOptions<RabbitMqOptions> rabbitMqOptions,
        ILogger<ProviderConsumerService> logger)
    {
        _mainQueue = mainQueue;
        _retryQueue = $"{mainQueue}.retry";
        _dlqQueue = $"{mainQueue}.dlq";
        _connectionFactory = connectionFactory;
        _scopeFactory = scopeFactory;
        _publisher = publisher;
        _queueOptions = queueOptions.Value;
        _reconnectDelaySeconds = rabbitMqOptions.Value.ConsumerReconnectDelaySeconds;
        _logger = logger;
    }

    /// <summary>
    /// Outer reconnect loop: if the channel closes unexpectedly (broker restart,
    /// network partition after recovery exhaustion), waits briefly then restarts.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunConsumerLoopAsync(stoppingToken);
                // RunConsumerLoopAsync returns normally only on clean shutdown.
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Consumer for queue {Queue} lost connection. Reconnecting in {Delay}s.",
                    _mainQueue, _reconnectDelaySeconds);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_reconnectDelaySeconds), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        _logger.LogInformation("Consumer stopped for queue: {Queue}", _mainQueue);
    }

    /// <summary>
    /// Acquires a fresh channel, registers the consumer, then blocks until
    /// the channel closes or <paramref name="stoppingToken"/> is cancelled.
    /// Throws if the channel closes unexpectedly (triggers the reconnect loop).
    /// </summary>
    private async Task RunConsumerLoopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Consumer connecting to queue: {Queue}", _mainQueue);

        var conn    = await _connectionFactory.GetConnectionAsync(stoppingToken);
        var channel = await conn.CreateChannelAsync(cancellationToken: stoppingToken);
        await channel.BasicQosAsync(0, 1, false, stoppingToken);

        // TCS that fires when the channel closes (abnormal = exception; clean = result).
        var channelClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var consumer = new AsyncEventingBasicConsumer(channel);

        consumer.ShutdownAsync += (_, args) =>
        {
            if (stoppingToken.IsCancellationRequested)
                channelClosed.TrySetResult();
            else
                channelClosed.TrySetException(
                    new Exception($"Channel shutdown (initiator={args.Initiator}): {args.ReplyText}"));
            return Task.CompletedTask;
        };

        consumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                await ProcessMessageAsync(ea, channel, stoppingToken);
            }
            catch (Exception ex)
            {
                // Never ACK on an unhandled exception — the publish to retry/DLQ may have
                // failed, so requeue to avoid data loss. CancellationToken.None ensures
                // shutdown does not prevent the nack.
                _logger.LogError(ex, "Unhandled error in consumer for queue {Queue}. Nacking with requeue.", _mainQueue);
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, CancellationToken.None);
            }
        };

        await channel.BasicConsumeAsync(_mainQueue, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);
        _logger.LogInformation("Consumer active on queue: {Queue}", _mainQueue);

        // Block until the channel closes or the host shuts down.
        using var stopReg = stoppingToken.Register(() => channelClosed.TrySetResult());
        await channelClosed.Task;

        try { await channel.DisposeAsync(); } catch { /* channel may already be closed */ }
    }

    private async Task ProcessMessageAsync(
        BasicDeliverEventArgs ea,
        IChannel channel,
        CancellationToken ct)
    {
        var body = Encoding.UTF8.GetString(ea.Body.ToArray());
        IntegrationJob? job;

        try
        {
            job = JsonSerializer.Deserialize<IntegrationJob>(body);
            if (job is null) throw new InvalidOperationException("Null job deserialized.");
        }
        catch (Exception ex)
        {
            // BUG-3 FIX: actually publish a poison-message envelope to DLQ.
            // Previous code only ACKed and logged "Sending to DLQ" — message was silently dropped.
            _logger.LogError(ex, "Failed to deserialize job from queue {Queue}. Publishing poison message to DLQ.", _mainQueue);
            var poison = new IntegrationJob
            {
                JobId         = ea.BasicProperties?.MessageId ?? Guid.NewGuid().ToString(),
                CorrelationId = ea.BasicProperties?.CorrelationId ?? string.Empty,
                // Use queue name as providerKey hint — we cannot deserialize the body,
                // but the queue name uniquely identifies which consumer/provider was targeted.
                ProviderKey   = _mainQueue,
                HotelId       = string.Empty,
                EventType     = "POISON",
                RawPayload    = body
            };
            // BUG-4 FIX: use CancellationToken.None so shutdown does not prevent the ACK/publish.
            await _publisher.PublishDlqAsync(poison, _dlqQueue, attempt: 1,
                errorCode: "DESER_FAILED", errorMessage: ex.Message, CancellationToken.None);
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, CancellationToken.None);
            return;
        }

        var attempt = GetAttempt(ea.BasicProperties.Headers);

        // Add structured log fields for every log entry inside this processing scope.
        using var logScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["Provider"]      = job.ProviderKey,
            ["CorrelationId"] = job.CorrelationId,
            ["Attempt"]       = attempt,
            ["QueueName"]     = _mainQueue
        });

        using var scope = _scopeFactory.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ProcessIntegrationJobHandler>();

        var result = await handler.HandleAsync(job, ct);

        // BUG-4 FIX: use CancellationToken.None for all ack/nack/publish so that a
        // graceful-shutdown cancellation does not leave messages unacknowledged.
        switch (result.Outcome)
        {
            case IntegrationOutcome.Success:
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, CancellationToken.None);
                break;

            case IntegrationOutcome.RetryableFailure:
                if (attempt + 1 > _queueOptions.MaxRetryAttempts)
                {
                    _logger.LogWarning(
                        "Max retries ({MaxRetry}) exceeded for job {JobId} on provider {Provider} queue {QueueName}. Sending to DLQ.",
                        _queueOptions.MaxRetryAttempts, job.JobId, job.ProviderKey, _mainQueue);
                    await _publisher.PublishDlqAsync(job, _dlqQueue, attempt, result.ErrorCode, result.ErrorMessage, CancellationToken.None);
                }
                else
                {
                    _logger.LogInformation(
                        "Scheduling retry {NextAttempt}/{MaxRetry} for job {JobId} on provider {Provider}.",
                        attempt + 1, _queueOptions.MaxRetryAttempts, job.JobId, job.ProviderKey);
                    await _publisher.PublishRetryAsync(job, _retryQueue, attempt, result.ErrorCode, result.ErrorMessage, CancellationToken.None);
                }
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, CancellationToken.None);
                break;

            case IntegrationOutcome.NonRetryableFailure:
                _logger.LogWarning(
                    "Non-retryable failure for job {JobId} on provider {Provider} queue {QueueName}. Sending to DLQ. ErrorCode={ErrorCode}",
                    job.JobId, job.ProviderKey, _mainQueue, result.ErrorCode);
                await _publisher.PublishDlqAsync(job, _dlqQueue, attempt, result.ErrorCode, result.ErrorMessage, CancellationToken.None);
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, CancellationToken.None);
                break;
        }
    }

    /// <summary>
    /// BUG-2 FIX: RabbitMQ broker may re-encode a 32-bit int header as a 64-bit long
    /// (AMQP type 'l') after TTL-based dead-lettering. The old <c>raw is int i</c>
    /// pattern silently fell back to 1 every time, causing an infinite retry loop.
    /// </summary>
    private static int GetAttempt(IDictionary<string, object?>? headers)
    {
        if (headers is null) return 1;
        if (!headers.TryGetValue(RabbitMqHeaders.RetryAttempt, out var raw)) return 1;
        return raw switch
        {
            int i    => i,
            long l   => (int)l,
            byte[] b => int.TryParse(Encoding.UTF8.GetString(b), out var parsed) ? parsed : 1,
            string s => int.TryParse(s, out var p) ? p : 1,
            _        => 1
        };
    }

    /// <summary>
    /// Reads a string header that the RabbitMQ.Client may return as <c>byte[]</c>.
    /// </summary>
    internal static string? GetStringHeader(IDictionary<string, object?>? headers, string key)
    {
        if (headers is null || !headers.TryGetValue(key, out var raw)) return null;
        return raw switch
        {
            string s  => s,
            byte[] b  => Encoding.UTF8.GetString(b),
            _         => raw?.ToString()
        };
    }
}
