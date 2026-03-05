using Microsoft.Extensions.DependencyInjection;
using PmsIntegration.Infrastructure.Http.DelegatingHandlers;

namespace PmsIntegration.Infrastructure.Http;

/// <summary>
/// Registers named HttpClients for each provider with shared delegating handlers.
/// Call RegisterProviderClient per provider DI extension.
/// </summary>
public static class ProviderHttpClientFactory
{
    public static IHttpClientBuilder RegisterProviderClient(
        this IServiceCollection services,
        string providerKey,
        Action<HttpClient>? configure = null)
    {
        return services
            .AddHttpClient(providerKey, client =>
            {
                configure?.Invoke(client);
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            })
            .AddHttpMessageHandler<CorrelationIdHandler>();
    }
}
