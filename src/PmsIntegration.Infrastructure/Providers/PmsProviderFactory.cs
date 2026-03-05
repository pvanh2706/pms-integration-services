using PmsIntegration.Core.Abstractions;

namespace PmsIntegration.Infrastructure.Providers;

/// <summary>
/// Singleton implementation of <see cref="IPmsProviderFactory"/>.
/// Populated automatically from all <see cref="IPmsProvider"/> instances registered in DI.
/// Each provider registers itself via:
///   <c>services.AddSingleton&lt;IPmsProvider, XxxProvider&gt;()</c>
/// This class requires no changes when a new provider is added.
/// </summary>
public sealed class PmsProviderFactory : IPmsProviderFactory
{
    private readonly Dictionary<string, IPmsProvider> _providers;

    public PmsProviderFactory(IEnumerable<IPmsProvider> providers)
    {
        _providers = new Dictionary<string, IPmsProvider>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in providers)
        {
            if (string.IsNullOrWhiteSpace(provider.ProviderKey))
                throw new ArgumentException(
                    $"A registered {nameof(IPmsProvider)} has a null or empty {nameof(IPmsProvider.ProviderKey)}. " +
                    $"Provider type: {provider.GetType().FullName}",
                    nameof(providers));

            if (_providers.ContainsKey(provider.ProviderKey))
                throw new ArgumentException(
                    $"Duplicate provider key '{provider.ProviderKey}' detected. " +
                    $"Provider type: {provider.GetType().FullName}",
                    nameof(providers));

            _providers[provider.ProviderKey] = provider;
        }
    }

    public IReadOnlyList<string> RegisteredKeys => _providers.Keys.ToList();

    public IReadOnlyCollection<string> GetRegisteredProviderCodes()
        => _providers.Keys.ToList().AsReadOnly();

    public IPmsProvider Get(string providerCode)
    {
        if (_providers.TryGetValue(providerCode, out var provider))
            return provider;

        throw new InvalidOperationException(
            $"No IPmsProvider registered for provider code '{providerCode}'. " +
            $"Registered codes: [{string.Join(", ", RegisteredKeys)}]");
    }
}
