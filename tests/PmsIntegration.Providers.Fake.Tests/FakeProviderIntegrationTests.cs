using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PmsIntegration.Core.Contracts;
using PmsIntegration.Providers.Fake.Mapping;
using Xunit;

namespace PmsIntegration.Providers.Fake.Tests;

/// <summary>
/// Integration tests for the full FakeProvider pipeline:
///   IntegrationJob → FakeRequestBuilder → FakeClient → ProviderResponse
///
/// These tests exercise the entire provider without any real network or
/// external dependencies. No DI container is required.
///
/// This is the test pattern to replicate for new providers — just swap
/// Fake* with YourProvider*.
/// </summary>
public sealed class FakeProviderIntegrationTests
{
    private static FakeProvider CreateProvider(bool simulateFailure = false)
    {
        var options = Options.Create(new FakeOptions
        {
            BaseUrl             = "https://fake.local",
            ApiKey              = "test-key",
            SimulateFailure     = simulateFailure,
            SimulatedStatusCode = 503
        });

        var mapper  = new FakeMapper();
        var builder = new FakeRequestBuilder(mapper, options);
        var client  = new FakeClient(options, NullLogger<FakeClient>.Instance);

        return new FakeProvider(builder, client);
    }

    [Fact]
    public void ProviderKey_ShouldBeFAKE()
    {
        CreateProvider().ProviderKey.Should().Be("FAKE");
    }

    [Fact]
    public async Task HandleJob_HappyPath_ReturnsSuccessResponse()
    {
        var provider = CreateProvider(simulateFailure: false);
        var job      = MakeJob();

        var request  = await provider.BuildRequestAsync(job);
        var response = await provider.SendAsync(request);

        response.IsSuccess.Should().BeTrue();
        response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task HandleJob_WhenSimulateFailure_ReturnsFailureResponse()
    {
        var provider = CreateProvider(simulateFailure: true);
        var job      = MakeJob();

        var request  = await provider.BuildRequestAsync(job);
        var response = await provider.SendAsync(request);

        response.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task BuildRequest_ShouldIncludeAllJobFields()
    {
        var provider = CreateProvider();
        var job      = MakeJob("hotel-A", "evt-B", "CHECKOUT");

        var request = await provider.BuildRequestAsync(job);

        request.ProviderKey.Should().Be("FAKE");
        request.Method.Should().Be("POST");
        request.Endpoint.Should().EndWith("/events");
        request.JsonBody.Should().Contain("hotel-A");
        request.Headers.Should().ContainKey("X-Api-Key");
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static IntegrationJob MakeJob(
        string hotelId   = "h1",
        string eventId   = "e1",
        string eventType = "TEST") =>
        new()
        {
            HotelId       = hotelId,
            EventId       = eventId,
            EventType     = eventType,
            CorrelationId = "corr-" + Guid.NewGuid().ToString("N")[..8],
            ProviderKey   = "FAKE"
        };
}
