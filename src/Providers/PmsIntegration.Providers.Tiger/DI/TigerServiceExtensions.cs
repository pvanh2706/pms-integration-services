using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PmsIntegration.Core.Abstractions;
using PmsIntegration.Providers.Tiger.Incoming;
using PmsIntegration.Providers.Tiger.Mapping;

namespace PmsIntegration.Providers.Tiger.DI;

public static class TigerServiceExtensions
{
    /// <summary>
    /// Registers the Tiger provider as an <see cref="IPmsProvider"/> in the DI container.
    /// Config section: <c>Providers:TIGER</c>
    /// </summary>
    public static IServiceCollection AddTigerProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<TigerOptions>(configuration.GetSection("Providers:TIGER"));

        services.AddHttpClient("TIGER", (sp, client) =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TigerOptions>>().Value;
            client.BaseAddress = new Uri(opts.BaseUrl);
            client.Timeout     = TimeSpan.FromSeconds(opts.TimeoutSeconds);
        });

        services.AddSingleton<TigerMapper>();
        services.AddSingleton<TigerRequestBuilder>();
        services.AddSingleton<TigerClient>();
        services.AddSingleton<IPmsProvider, TigerProvider>();

        // Incoming: handles SOAP messages received from TigerTMS
        services.AddSingleton<TigerIncomingMessageDispatcher>();

        return services;
    }
}
