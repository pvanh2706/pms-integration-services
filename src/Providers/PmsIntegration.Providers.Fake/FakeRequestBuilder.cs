using Microsoft.Extensions.Options;
using PmsIntegration.Core.Abstractions;
using PmsIntegration.Core.Contracts;
using PmsIntegration.Providers.Fake.Mapping;

namespace PmsIntegration.Providers.Fake;

public sealed class FakeRequestBuilder : IPmsRequestBuilder
{
    public string ProviderKey => "FAKE";

    private readonly FakeMapper _mapper;
    private readonly FakeOptions _options;

    public FakeRequestBuilder(FakeMapper mapper, IOptions<FakeOptions> options)
    {
        _mapper = mapper;
        _options = options.Value;
    }

    public Task<ProviderRequest> BuildAsync(IntegrationJob job, CancellationToken ct = default)
    {
        var request = new ProviderRequest
        {
            ProviderKey = ProviderKey,
            CorrelationId = job.CorrelationId,
            Method = "POST",
            Endpoint = $"{_options.BaseUrl.TrimEnd('/')}/events",
            JsonBody = _mapper.Map(job),
            Headers =
            {
                ["X-Api-Key"] = _options.ApiKey
            }
        };

        return Task.FromResult(request);
    }
}
