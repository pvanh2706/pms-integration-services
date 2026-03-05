using PmsIntegration.Core.Contracts;
using PmsIntegration.Providers.Abstractions;

namespace PmsIntegration.Providers.Opera;

/// <summary>
/// Pluggable Opera Cloud provider module.
/// Register via <c>services.AddOperaProvider(config)</c>.
///
/// Orchestrates two focused collaborators:
///   - <see cref="OperaRequestBuilder"/>  pure mapping (IntegrationJob → ProviderRequest)
///   - <see cref="OperaClient"/>          real HTTP transport + OAuth auth
///
/// All Opera-specific logic stays inside this project.
/// </summary>
public sealed class OperaProvider : PmsProviderBase
{
    public override string ProviderKey => "OPERA";

    private readonly OperaRequestBuilder _requestBuilder;
    private readonly OperaClient _client;

    public OperaProvider(OperaRequestBuilder requestBuilder, OperaClient client)
    {
        _requestBuilder = requestBuilder;
        _client         = client;
    }

    public override Task<ProviderRequest> BuildRequestAsync(IntegrationJob job, CancellationToken ct = default)
        => _requestBuilder.BuildAsync(job, ct);

    public override Task<ProviderResponse> SendAsync(ProviderRequest request, CancellationToken ct = default)
        => _client.SendAsync(request, ct);
}
