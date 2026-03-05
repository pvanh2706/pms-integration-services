using PmsIntegration.Core.Contracts;
using PmsIntegration.Providers.Abstractions;

namespace PmsIntegration.Providers.Tiger;

/// <summary>
/// Pluggable Tiger provider module.
/// Register via <c>services.AddTigerProvider(config)</c>.
///
/// Orchestrates two focused collaborators:
///   - <see cref="TigerRequestBuilder"/>  pure mapping (IntegrationJob → ProviderRequest)
///   - <see cref="TigerClient"/>          real HTTP transport
///
/// All Tiger-specific logic stays inside this project.
/// </summary>
public sealed class TigerProvider : PmsProviderBase
{
    public override string ProviderKey => "TIGER";

    private readonly TigerRequestBuilder _requestBuilder;
    private readonly TigerClient _client;

    public TigerProvider(TigerRequestBuilder requestBuilder, TigerClient client)
    {
        _requestBuilder = requestBuilder;
        _client = client;
    }

    public override Task<ProviderRequest> BuildRequestAsync(IntegrationJob job, CancellationToken ct = default)
        => _requestBuilder.BuildAsync(job, ct);

    public override Task<ProviderResponse> SendAsync(ProviderRequest request, CancellationToken ct = default)
        => _client.SendAsync(request, ct);
}
