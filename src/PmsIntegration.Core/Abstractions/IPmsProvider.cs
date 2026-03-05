using PmsIntegration.Core.Contracts;

namespace PmsIntegration.Core.Abstractions;

/// <summary>
/// Primary abstraction for a pluggable PMS provider module.
/// Each provider is a self-contained unit: it knows how to map an IntegrationJob
/// into a provider-specific request AND how to send it.
///
/// Providers must not depend on each other.
/// All provider-specific logic lives inside the provider's own folder.
/// </summary>
public interface IPmsProvider
{
    /// <summary>Normalized uppercase provider key, e.g. "FAKE", "TIGER", "OPERA".</summary>
    string ProviderKey { get; }

    /// <summary>
    /// Maps an <see cref="IntegrationJob"/> to a provider-specific <see cref="ProviderRequest"/>.
    /// No HTTP — pure mapping only.
    /// </summary>
    Task<ProviderRequest> BuildRequestAsync(IntegrationJob job, CancellationToken ct = default);

    /// <summary>
    /// Sends the <see cref="ProviderRequest"/> to the external provider API
    /// and returns the raw <see cref="ProviderResponse"/>.
    /// </summary>
    Task<ProviderResponse> SendAsync(ProviderRequest request, CancellationToken ct = default);
}
