using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PmsIntegration.Core.Abstractions;
using PmsIntegration.Providers.Fake.Mapping;

namespace PmsIntegration.Providers.Fake.DI;

public static class FakeServiceExtensions
{
    public static IServiceCollection AddFakeProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<FakeOptions>(configuration.GetSection("Providers:FAKE"));

        services.AddSingleton<FakeMapper>();
        services.AddSingleton<FakeRequestBuilder>();
        services.AddSingleton<FakeClient>();
        services.AddSingleton<IPmsProvider, FakeProvider>();

        return services;
    }
}
