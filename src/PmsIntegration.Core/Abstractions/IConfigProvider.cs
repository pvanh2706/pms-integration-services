namespace PmsIntegration.Core.Abstractions;

/// <summary>
/// Provides configuration values at runtime.
/// Implementations: AppSettingsConfigProvider, DbConfigProvider.
/// </summary>
public interface IConfigProvider
{
    string? Get(string key);
    T? GetSection<T>(string sectionKey) where T : class;
}
