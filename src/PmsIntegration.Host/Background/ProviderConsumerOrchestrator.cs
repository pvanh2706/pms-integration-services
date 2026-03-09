using Microsoft.Extensions.Options;
using PmsIntegration.Core.Abstractions;
using PmsIntegration.Infrastructure.Options;
using PmsIntegration.Infrastructure.RabbitMq;

namespace PmsIntegration.Host.Background;

/// <summary>
/// Class quản lý các consumer của từng provider (Bộ điều phối các queue consumer cho từng PMS provider.)
/// File này khởi tạo và quản lý tất cả RabbitMQ consumers cho từng PMS provider khi service start.
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
        IPmsProviderFactory providerFactory, // Lấy danh sách providers đã đăng ký để tạo consumer tương ứng
        RabbitMqConnectionFactory connectionFactory, // Factory để tạo kết nối RabbitMQ cho mỗi consumer
        RabbitMqTopology topology, // Topology để declare queues trước khi consume
        IServiceScopeFactory scopeFactory, // Factory để tạo scope DI cho mỗi consumer, đảm bảo lifetime của dependencies
        IQueuePublisher publisher, // Publisher để mỗi consumer có thể publish message nếu cần (ví dụ: dead-lettering)
        IOptions<QueueOptions> queueOptions, // Lấy cấu hình queue từ appsettings để xác định tên queue cho mỗi provider
        IOptions<RabbitMqOptions> rabbitMqOptions, // Lấy cấu hình RabbitMQ để truyền vào mỗi consumer
        ILoggerFactory loggerFactory) // Logger factory để tạo logger cho orchestrator và từng consumer, giúp theo dõi hoạt động và lỗi của từng consumer riêng biệt
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

            // Consumers are created manually because each provider requires
            // a dedicated queue consumer with a runtime queue name.
            //
            // The DI container cannot automatically construct multiple instances
            // of the same service type with different parameters.
            //
            // This orchestrator therefore acts as a small factory.
            // If consumer initialization becomes more complex (retry policies,
            // concurrency limits, dead-letter queues, etc.), consider extracting
            // this logic into an IProviderConsumerFactory.
            return new ProviderConsumerService(
                queueName,
                connectionFactory,
                topology,
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
