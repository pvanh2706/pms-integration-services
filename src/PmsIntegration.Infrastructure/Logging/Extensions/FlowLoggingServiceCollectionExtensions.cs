using Microsoft.Extensions.DependencyInjection;
using PmsIntegration.Infrastructure.Logging.Flow;

namespace PmsIntegration.Infrastructure.Logging.Extensions;

/// <summary>
/// DI registration for the Flow Logging feature.
/// Call this from <c>AddInfrastructure(...)</c> or directly from <c>Program.cs</c>.
///
/// Lifetime choice:
///   - IApiFlowLogger → Scoped: one instance per HTTP request.
///     The HttpContext scope ensures no cross-request state bleed.
///   - IProviderFlowLogger → Scoped: one instance per IServiceScope.
///     ProviderConsumerService creates a new scope per RabbitMQ message,
///     so each message gets its own isolated ProviderFlowLogger.
/// </summary>
public static class FlowLoggingServiceCollectionExtensions
{
    public static IServiceCollection AddFlowLogging(this IServiceCollection services)
    {
        services.AddScoped<IApiFlowLogger, ApiFlowLogger>();
        services.AddScoped<IProviderFlowLogger, ProviderFlowLogger>();
        return services;
    }
}
