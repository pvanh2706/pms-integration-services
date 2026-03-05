using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PmsIntegration.Core.Abstractions;
using PmsIntegration.Providers.Opera.Mapping;

namespace PmsIntegration.Providers.Opera.DI;

public static class OperaServiceExtensions
{
    /// <summary>
    /// Registers the Opera provider as an <see cref="IPmsProvider"/> in the DI container.
    /// Config section: <c>Providers:OPERA</c>
    /// </summary>
    public static IServiceCollection AddOperaProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<OperaOptions>(configuration.GetSection("Providers:OPERA"));

        services.AddHttpClient("OPERA", (sp, client) =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OperaOptions>>().Value;
            client.BaseAddress = new Uri(opts.BaseUrl);
            client.Timeout     = TimeSpan.FromSeconds(opts.TimeoutSeconds);
        });

        services.AddSingleton<OperaMapper>();
        services.AddSingleton<OperaRequestBuilder>();
        services.AddSingleton<OperaClient>();
        services.AddSingleton<IPmsProvider, OperaProvider>();

        return services;
    }
}
