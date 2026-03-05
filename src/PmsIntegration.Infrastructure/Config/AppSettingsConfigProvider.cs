using Microsoft.Extensions.Configuration;
using PmsIntegration.Core.Abstractions;

namespace PmsIntegration.Infrastructure.Config;

/// <summary>
/// Reads configuration from appsettings.json / environment variables.
/// </summary>
public sealed class AppSettingsConfigProvider : IConfigProvider
{
    private readonly IConfiguration _configuration;

    public AppSettingsConfigProvider(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string? Get(string key) => _configuration[key];

    public T? GetSection<T>(string sectionKey) where T : class =>
        _configuration.GetSection(sectionKey).Get<T>();
}
