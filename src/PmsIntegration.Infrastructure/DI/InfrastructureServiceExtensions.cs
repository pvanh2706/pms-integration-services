using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PmsIntegration.Core.Abstractions;
using PmsIntegration.Infrastructure.Clock;
using PmsIntegration.Infrastructure.Config;
using PmsIntegration.Infrastructure.Http.DelegatingHandlers;
using PmsIntegration.Infrastructure.Idempotency;
using PmsIntegration.Infrastructure.Logging;
using PmsIntegration.Infrastructure.Logging.Extensions;
using PmsIntegration.Infrastructure.Options;
using PmsIntegration.Infrastructure.RabbitMq;

namespace PmsIntegration.Infrastructure.DI;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Options
        services.Configure<RabbitMqOptions>(configuration.GetSection("RabbitMq"));
        services.Configure<QueueOptions>(configuration.GetSection("Queues"));

        // Clock
        services.AddSingleton<IClock, SystemClock>();

        // Config
        services.AddSingleton<IConfigProvider, AppSettingsConfigProvider>();

        // Audit logger
        services.AddSingleton<IAuditLogger, ElasticAuditLogger>();

        // Idempotency — default: in-memory; swap for RedisIdempotencyStore in production
        services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();

        // RabbitMQ
        services.AddSingleton<RabbitMqConnectionFactory>();
        services.AddSingleton<RabbitMqTopology>();
        services.AddSingleton<IQueuePublisher, RabbitMqQueuePublisher>();

        // HTTP handlers
        services.AddTransient<CorrelationIdHandler>();

        // Flow logging — writes API_FLOW and PROVIDER_FLOW documents to Elasticsearch via Serilog
        services.AddFlowLogging();

        return services;
    }
}
