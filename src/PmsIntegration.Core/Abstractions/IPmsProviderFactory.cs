namespace PmsIntegration.Core.Abstractions;

/// <summary>
/// Central factory that resolves any registered <see cref="IPmsProvider"/> by its string key.
/// Populated automatically from all <see cref="IPmsProvider"/> registrations in the DI container.
/// No switch/case required in calling code — just call <see cref="Get"/>.
/// </summary>
public interface IPmsProviderFactory
{
    /// <summary>
    /// Returns the provider for the given key (case-insensitive).
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when no provider is registered for the key.</exception>
    IPmsProvider Get(string providerCode);

    /// <summary>All provider codes currently registered.</summary>
    IReadOnlyList<string> RegisteredKeys { get; }

    /// <summary>
    /// Returns all registered provider codes.
    /// Intended for diagnostics and health-check endpoints.
    /// </summary>
    IReadOnlyCollection<string> GetRegisteredProviderCodes();
}
