using Microsoft.Extensions.Options;
using PmsIntegration.Core.Abstractions;
using PmsIntegration.Core.Contracts;
using PmsIntegration.Providers.Tiger.Mapping;

namespace PmsIntegration.Providers.Tiger;

/// <summary>
/// Builds the Tiger provider HTTP request from an <see cref="IntegrationJob"/>.
/// Pure mapping — no I/O.
/// </summary>
public sealed class TigerRequestBuilder : IPmsRequestBuilder
{
    public string ProviderKey => "TIGER";

    private readonly TigerMapper _mapper;
    private readonly TigerOptions _options;

    public TigerRequestBuilder(TigerMapper mapper, IOptions<TigerOptions> options)
    {
        _mapper = mapper;
        _options = options.Value;
    }

    public Task<ProviderRequest> BuildAsync(IntegrationJob job, CancellationToken ct = default)
    {
        if (job.EventType.Equals("Checkin", StringComparison.OrdinalIgnoreCase))
        {
            var soapRequest = new ProviderRequest
            {
                ProviderKey   = ProviderKey,
                CorrelationId = job.CorrelationId,
                Method        = "POST",
                Endpoint      = _options.SoapEndpoint,
                JsonBody      = TigerCheckinSoapBuilder.Build(job, _options.WsUserKey),
                Headers       = { ["Content-Type"] = "text/xml" }
            };

            return Task.FromResult(soapRequest);
        }

        var request = new ProviderRequest
        {
            ProviderKey   = ProviderKey,
            CorrelationId = job.CorrelationId,
            Method        = "POST",
            Endpoint      = $"{_options.BaseUrl.TrimEnd('/')}/v1/events",
            JsonBody      = _mapper.Map(job),
            Headers       = { ["X-Api-Key"] = _options.ApiKey }
        };

        return Task.FromResult(request);
    }
}
