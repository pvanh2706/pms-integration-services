using Microsoft.Extensions.Options;
using PmsIntegration.Core.Abstractions;
using PmsIntegration.Core.Contracts;
using PmsIntegration.Providers.Opera.Mapping;

namespace PmsIntegration.Providers.Opera;

/// <summary>
/// Builds the Opera Cloud provider HTTP request from an <see cref="IntegrationJob"/>.
/// Pure mapping — no I/O.
/// Authorization headers are added at the transport layer inside <see cref="OperaClient"/>.
/// </summary>
public sealed class OperaRequestBuilder : IPmsRequestBuilder
{
    public string ProviderKey => "OPERA";

    private readonly OperaMapper _mapper;
    private readonly OperaOptions _options;

    public OperaRequestBuilder(OperaMapper mapper, IOptions<OperaOptions> options)
    {
        _mapper  = mapper;
        _options = options.Value;
    }

    public Task<ProviderRequest> BuildAsync(IntegrationJob job, CancellationToken ct = default)
    {
        var request = new ProviderRequest
        {
            ProviderKey   = ProviderKey,
            CorrelationId = job.CorrelationId,
            Method        = "POST",
            Endpoint      = $"{_options.BaseUrl.TrimEnd('/')}/api/v1/pms-events",
            JsonBody      = _mapper.Map(job)
            // NOTE: Authorization Bearer token is added by OperaClient (OAuth client-credentials)
        };

        return Task.FromResult(request);
    }
}
