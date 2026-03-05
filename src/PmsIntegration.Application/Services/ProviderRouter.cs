using PmsIntegration.Core.Abstractions;

namespace PmsIntegration.Application.Services;

/// <summary>
/// Resolves the main queue name for a given provider key via IConfigProvider.
/// Config path: Queues:ProviderQueues:{PROVIDER_KEY}
/// </summary>
public sealed class ProviderRouter
{
    private readonly IConfigProvider _config;

    public ProviderRouter(IConfigProvider config)
    {
        _config = config;
    }

    public string ResolveQueue(string providerKey)
    {
        var normalized = providerKey.Trim().ToUpperInvariant();
        var configuredQueue = _config.Get($"Queues:ProviderQueues:{normalized}");

        if (!string.IsNullOrWhiteSpace(configuredQueue))
            return configuredQueue;

        // Default: q.pms.<providerKeyLower>
        return $"q.pms.{normalized.ToLowerInvariant()}";
    }

    public string ResolveRetryQueue(string mainQueue) => $"{mainQueue}.retry";
    public string ResolveDlqQueue(string mainQueue) => $"{mainQueue}.dlq";
}
