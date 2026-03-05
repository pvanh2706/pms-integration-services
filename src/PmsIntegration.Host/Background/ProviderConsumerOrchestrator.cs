using Microsoft.Extensions.Options;
using PmsIntegration.Core.Abstractions;
using PmsIntegration.Infrastructure.Options;
using PmsIntegration.Infrastructure.RabbitMq;

namespace PmsIntegration.Host.Background;

/// <summary>
/// Hosted service that automatically starts one <see cref="ProviderConsumerService"/>
/// per registered <see cref="IPmsProvider"/>.
///
/// When a new provider is added and registered via <c>AddXxxProvider()</c>,
/// a queue consumer for it is created here with zero host-level changes.
/// Queue name resolution order:
///   1. appsettings: Queues:ProviderQueues:{PROVIDER_CODE}
///   2. Default convention: q.pms.{providercode_lowercase}
/// </summary>
public sealed class ProviderConsumerOrchestrator : IHostedService
{
    private readonly List<ProviderConsumerService> _consumers;
    private readonly ILogger<ProviderConsumerOrchestrator> _logger;

    public ProviderConsumerOrchestrator(
        IPmsProviderFactory providerFactory,
        RabbitMqConnectionFactory connectionFactory,
        IServiceScopeFactory scopeFactory,
        IQueuePublisher publisher,
        IOptions<QueueOptions> queueOptions,
        IOptions<RabbitMqOptions> rabbitMqOptions,
        ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<ProviderConsumerOrchestrator>();

        _consumers = providerFactory.RegisteredKeys.Select(providerCode =>
        {
            var queueName = queueOptions.Value.ProviderQueues.TryGetValue(providerCode, out var q)
                ? q
                : $"q.pms.{providerCode.ToLowerInvariant()}";

            _logger.LogInformation(
                "Registering consumer for provider '{Provider}' on queue '{Queue}'",
                providerCode, queueName);

            return new ProviderConsumerService(
                queueName,
                connectionFactory,
                scopeFactory,
                publisher,
                queueOptions,
                rabbitMqOptions,
                loggerFactory.CreateLogger<ProviderConsumerService>());
        }).ToList();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var consumer in _consumers)
            await consumer.StartAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var consumer in _consumers)
            await consumer.StopAsync(cancellationToken);
    }
}
