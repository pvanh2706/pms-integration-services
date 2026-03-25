using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PmsIntegration.Core.Abstractions;
using PmsIntegration.Infrastructure.Providers;
using PmsIntegration.Providers.Fake.DI;
using PmsIntegration.Providers.Tiger.DI;
using PmsIntegration.Providers.Opera.DI;

namespace PmsIntegration.Host.Providers;

/// <summary>
/// Single composition-root entry point for the entire provider plugin system.
/// Host calls <c>services.AddProviders(config)</c> exactly once.
///
/// Responsibilities:
///   1. Registers every provider plugin (each injects its own <see cref="IPmsProvider"/>).
///   2. Registers <see cref="IPmsProviderFactory"/> so Application + Host can resolve
///      providers by code without any switch/case.
///
/// To add a new provider:
///   1. Create project PmsIntegration.Providers.Acme
///   2. Implement IPmsProvider + Add<Acme>Provider() extension method
///   3. Add ONE line here: services.AddAcmeProvider(configuration)
///   4. Add ONE ProjectReference to Host.csproj
///   No other files change.
/// </summary>
public static class ProvidersServiceExtensions
{
    public static IServiceCollection AddProviders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Register each provider plugin ────────────────────────────────
        // Each call registers: Options, HttpClient (if needed), Mapper,
        // RequestBuilder, Client, and services.AddSingleton<IPmsProvider, XxxProvider>().
        
        // services.AddFakeProvider(configuration);
        services.AddTigerProvider(configuration);
        services.AddOperaProvider(configuration);

        // ── Register the factory that collects all IPmsProvider instances ─
        // PmsProviderFactory receives IEnumerable<IPmsProvider> from DI
        // and builds a case-insensitive dictionary keyed by ProviderCode.
        // Must be registered AFTER the provider plugins above.
        services.AddSingleton<IPmsProviderFactory, PmsProviderFactory>();

        return services;
    }
}
