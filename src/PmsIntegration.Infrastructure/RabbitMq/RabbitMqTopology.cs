using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using PmsIntegration.Infrastructure.Options;

namespace PmsIntegration.Infrastructure.RabbitMq;

/// <summary>
/// Declares main/retry/dlq queues for a provider on startup.
/// </summary>
public sealed class RabbitMqTopology
{
    private readonly RabbitMqConnectionFactory _factory;
    private readonly QueueOptions _queueOptions;

    public RabbitMqTopology(RabbitMqConnectionFactory factory, IOptions<QueueOptions> queueOptions)
    {
        _factory = factory;
        _queueOptions = queueOptions.Value;
    }

    public async Task DeclareProviderQueuesAsync(string mainQueue, CancellationToken ct = default)
    {
        var retryQueue = $"{mainQueue}.retry";
        var dlqQueue = $"{mainQueue}.dlq";

        var conn = await _factory.GetConnectionAsync(ct);
        await using var channel = await conn.CreateChannelAsync(cancellationToken: ct);

        // DLQ
        await channel.QueueDeclareAsync(dlqQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: null, cancellationToken: ct);

        // Retry → dead-letters back to main
        var retryArgs = new Dictionary<string, object?>
        {
            ["x-message-ttl"] = (int)TimeSpan.FromSeconds(_queueOptions.RetryDelaySeconds).TotalMilliseconds,
            ["x-dead-letter-exchange"] = "",
            ["x-dead-letter-routing-key"] = mainQueue
        };
        await channel.QueueDeclareAsync(retryQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: retryArgs, cancellationToken: ct);

        // Main
        await channel.QueueDeclareAsync(mainQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: null, cancellationToken: ct);
    }
}
