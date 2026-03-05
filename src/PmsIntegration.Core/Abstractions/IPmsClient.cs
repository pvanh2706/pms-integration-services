using PmsIntegration.Core.Contracts;

namespace PmsIntegration.Core.Abstractions;

/// <summary>
/// HTTP transport layer for a single provider.
/// Each provider implements this to send a <see cref="ProviderRequest"/> and return a <see cref="ProviderResponse"/>.
/// No mapping logic — pure I/O only.
/// </summary>
public interface IPmsClient
{
    string ProviderKey { get; }
    Task<ProviderResponse> SendAsync(ProviderRequest request, CancellationToken ct = default);
}
