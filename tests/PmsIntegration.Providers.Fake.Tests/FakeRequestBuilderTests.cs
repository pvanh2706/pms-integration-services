using FluentAssertions;
using Microsoft.Extensions.Options;
using PmsIntegration.Core.Contracts;
using PmsIntegration.Providers.Fake.Mapping;
using Xunit;

namespace PmsIntegration.Providers.Fake.Tests;

/// <summary>
/// Unit tests for FakeRequestBuilder.
/// Verifies that endpoint, method, auth headers, and body are assembled correctly.
/// Pure mapping — no network calls, no DI container required.
/// </summary>
public sealed class FakeRequestBuilderTests
{
    private static FakeRequestBuilder CreateSut(
        string baseUrl = "https://fake.local",
        string apiKey  = "test-key")
    {
        var options = Options.Create(new FakeOptions
        {
            BaseUrl = baseUrl,
            ApiKey  = apiKey
        });
        return new FakeRequestBuilder(new FakeMapper(), options);
    }

    [Fact]
    public async Task BuildAsync_ShouldSetCorrectEndpoint()
    {
        var sut = CreateSut(baseUrl: "https://fake.local/");
        var job = MakeJob("hotel-1", "evt-1", "RES_CREATED");

        var request = await sut.BuildAsync(job);

        request.Endpoint.Should().Be("https://fake.local/events");
    }

    [Fact]
    public async Task BuildAsync_ShouldSetProviderKeyToFAKE()
    {
        var sut = CreateSut();
        var request = await sut.BuildAsync(MakeJob());

        request.ProviderKey.Should().Be("FAKE");
    }

    [Fact]
    public async Task BuildAsync_ShouldSetMethodToPOST()
    {
        var sut = CreateSut();
        var request = await sut.BuildAsync(MakeJob());

        request.Method.Should().Be("POST");
    }

    [Fact]
    public async Task BuildAsync_ShouldAddApiKeyHeader()
    {
        var sut = CreateSut(apiKey: "secret-123");
        var request = await sut.BuildAsync(MakeJob());

        request.Headers.Should().ContainKey("X-Api-Key")
            .WhoseValue.Should().Be("secret-123");
    }

    [Fact]
    public async Task BuildAsync_ShouldPopulateJsonBody()
    {
        var sut = CreateSut();
        var request = await sut.BuildAsync(MakeJob("hotel-2", "evt-2", "CHECK_IN"));

        request.JsonBody.Should().NotBeNullOrWhiteSpace();
        request.JsonBody.Should().Contain("hotel-2");
    }

    [Fact]
    public async Task BuildAsync_ShouldPreserveCorrelationId()
    {
        var sut = CreateSut();
        var job = MakeJob();
        job.CorrelationId = "trace-xyz";

        var request = await sut.BuildAsync(job);

        request.CorrelationId.Should().Be("trace-xyz");
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static IntegrationJob MakeJob(
        string hotelId    = "h1",
        string eventId    = "e1",
        string eventType  = "TEST") =>
        new()
        {
            HotelId       = hotelId,
            EventId       = eventId,
            EventType     = eventType,
            CorrelationId = Guid.NewGuid().ToString(),
            ProviderKey   = "FAKE"
        };
}
