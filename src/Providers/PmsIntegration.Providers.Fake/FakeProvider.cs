using PmsIntegration.Core.Abstractions;
using PmsIntegration.Core.Contracts;

namespace PmsIntegration.Providers.Fake;

/// <summary>
/// Reference implementation of <see cref="IPmsProvider"/> for the Fake provider.
///
/// This is the canonical reference class that junior developers should copy
/// when building a new provider. It delegates every concern to a focused collaborator:
///   - <see cref="FakeRequestBuilder"/>  maps IntegrationJob → ProviderRequest (no I/O)
///   - <see cref="FakeClient"/>          sends the request (simulated HTTP, no real network)
///
/// Inheriting a base class is optional — implementing IPmsProvider directly is fine.
/// </summary>
public sealed class FakeProvider : IPmsProvider
{
    public string ProviderKey => "FAKE";

    private readonly FakeRequestBuilder _requestBuilder;
    private readonly FakeClient _client;

    public FakeProvider(FakeRequestBuilder requestBuilder, FakeClient client)
    {
        _requestBuilder = requestBuilder;
        _client = client;
    }

    public Task<ProviderRequest> BuildRequestAsync(IntegrationJob job, CancellationToken ct = default)
        => _requestBuilder.BuildAsync(job, ct);

    public Task<ProviderResponse> SendAsync(ProviderRequest request, CancellationToken ct = default)
        => _client.SendAsync(request, ct);
}
