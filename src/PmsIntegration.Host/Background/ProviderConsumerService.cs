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
    private readonly ILogger<ProviderConsumerService> _logger;

    public ProviderConsumerService(
        string mainQueue,
        RabbitMqConnectionFactory connectionFactory,
        IServiceScopeFactory scopeFactory,
        IQueuePublisher publisher,
        IOptions<QueueOptions> queueOptions,
        ILogger<ProviderConsumerService> logger)
    {
        _mainQueue = mainQueue;
        _retryQueue = $"{mainQueue}.retry";
        _dlqQueue = $"{mainQueue}.dlq";
        _connectionFactory = connectionFactory;
        _scopeFactory = scopeFactory;
        _publisher = publisher;
        _queueOptions = queueOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Consumer starting for queue: {Queue}", _mainQueue);

        var conn = await _connectionFactory.GetConnectionAsync(stoppingToken);
        var channel = await conn.CreateChannelAsync(cancellationToken: stoppingToken);
        await channel.BasicQosAsync(0, 1, false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                await ProcessMessageAsync(ea, channel, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in consumer for queue {Queue}", _mainQueue);
                await channel.BasicAckAsync(ea.DeliveryTag, false, stoppingToken);
            }
        };

        await channel.BasicConsumeAsync(_mainQueue, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);

        // Block until the service is stopped
        await Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { });

        await channel.DisposeAsync();
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
            _logger.LogError(ex, "Failed to deserialize job from queue {Queue}. Sending to DLQ.", _mainQueue);
            await channel.BasicAckAsync(ea.DeliveryTag, false, ct);
            return;
        }

        var attempt = GetAttempt(ea.BasicProperties.Headers);

        using var scope = _scopeFactory.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ProcessIntegrationJobHandler>();

        var result = await handler.HandleAsync(job, ct);

        switch (result.Outcome)
        {
            case IntegrationOutcome.Success:
                await channel.BasicAckAsync(ea.DeliveryTag, false, ct);
                break;

            case IntegrationOutcome.RetryableFailure:
                if (attempt + 1 > _queueOptions.MaxRetryAttempts)
                {
                    _logger.LogWarning("Max retries exceeded for job {JobId}. Sending to DLQ.", job.JobId);
                    await _publisher.PublishDlqAsync(job, _dlqQueue, attempt, result.ErrorCode, result.ErrorMessage, ct);
                }
                else
                {
                    await _publisher.PublishRetryAsync(job, _retryQueue, attempt, result.ErrorCode, result.ErrorMessage, ct);
                }
                await channel.BasicAckAsync(ea.DeliveryTag, false, ct);
                break;

            case IntegrationOutcome.NonRetryableFailure:
                _logger.LogWarning("Non-retryable failure for job {JobId}. Sending to DLQ.", job.JobId);
                await _publisher.PublishDlqAsync(job, _dlqQueue, attempt, result.ErrorCode, result.ErrorMessage, ct);
                await channel.BasicAckAsync(ea.DeliveryTag, false, ct);
                break;
        }
    }

    private static int GetAttempt(IDictionary<string, object?>? headers)
    {
        if (headers is null) return 1;
        if (headers.TryGetValue(RabbitMqHeaders.RetryAttempt, out var raw) && raw is int i)
            return i;
        return 1;
    }
}
