using PmsIntegration.Core.Contracts;

namespace PmsIntegration.Core.Abstractions;

/// <summary>
/// Mapping layer for a single provider.
/// Transforms an <see cref="IntegrationJob"/> into a provider-specific <see cref="ProviderRequest"/>.
/// Must be pure (no I/O, no side-effects).
/// </summary>
public interface IPmsRequestBuilder
{
    string ProviderKey { get; }
    Task<ProviderRequest> BuildAsync(IntegrationJob job, CancellationToken ct = default);
}
