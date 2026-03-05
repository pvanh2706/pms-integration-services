using PmsIntegration.Core.Abstractions;
using PmsIntegration.Core.Contracts;

namespace PmsIntegration.Providers.Abstractions;

/// <summary>
/// Optional convenience base class for provider implementations.
/// Separates the mapping (BuildRequestAsync) and transport (SendAsync) concerns
/// while still exposing a single <see cref="IPmsProvider"/> surface.
///
/// Providers are NOT required to inherit this class; they may implement
/// <see cref="IPmsProvider"/> directly (see FakeProvider for that pattern).
/// </summary>
public abstract class PmsProviderBase : IPmsProvider
{
    public abstract string ProviderKey { get; }

    /// <summary>Pure mapping: IntegrationJob → ProviderRequest. No I/O.</summary>
    public abstract Task<ProviderRequest> BuildRequestAsync(IntegrationJob job, CancellationToken ct = default);

    /// <summary>HTTP transport: send ProviderRequest → ProviderResponse.</summary>
    public abstract Task<ProviderResponse> SendAsync(ProviderRequest request, CancellationToken ct = default);
}
