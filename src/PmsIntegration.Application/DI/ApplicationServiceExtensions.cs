using Microsoft.Extensions.DependencyInjection;
using PmsIntegration.Application.Services;
using PmsIntegration.Application.UseCases;

namespace PmsIntegration.Application.DI;

public static class ApplicationServiceExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddTransient<ReceivePmsEventHandler>();
        services.AddTransient<ProcessIntegrationJobHandler>();
        services.AddSingleton<ProviderRouter>();
        services.AddSingleton<EventValidator>();
        services.AddSingleton<RetryClassifier>();
        return services;
    }
}
