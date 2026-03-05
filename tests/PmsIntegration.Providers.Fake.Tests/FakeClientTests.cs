using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PmsIntegration.Core.Contracts;
using Xunit;

namespace PmsIntegration.Providers.Fake.Tests;

/// <summary>
/// Integration-style tests for FakeClient.
/// FakeClient simulates HTTP without a real network. For providers that use a
/// real HttpClient (Tiger, Opera), replace FakeClient with the real client and
/// use RichardSzalay.MockHttp to intercept HTTP calls (see the pattern below).
/// </summary>
public sealed class FakeClientTests
{
    // ── Success path ──────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_WhenSimulateFailureIsFalse_Returns200()
    {
        var client = CreateClient(simulateFailure: false);
        var request = MakeRequest();

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(200);
        response.IsSuccess.Should().BeTrue();
        response.Body.Should().Contain("accepted");
    }

    // ── Failure simulation path ───────────────────────────────────────────

    [Theory]
    [InlineData(503)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task SendAsync_WhenSimulateFailureIsTrue_ReturnsConfiguredStatusCode(int statusCode)
    {
        var client = CreateClient(simulateFailure: true, simulatedStatusCode: statusCode);
        var request = MakeRequest();

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(statusCode);
        response.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_WhenSimulateFailure_ResponseBodyContainsErrorInfo()
    {
        var client = CreateClient(simulateFailure: true, simulatedStatusCode: 503);
        var request = MakeRequest();

        var response = await client.SendAsync(request);

        response.Body.Should().Contain("simulated failure");
    }

    // ── ProviderKey ───────────────────────────────────────────────────────

    [Fact]
    public void ProviderKey_ShouldBeFAKE()
    {
        var client = CreateClient();

        client.ProviderKey.Should().Be("FAKE");
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static FakeClient CreateClient(
        bool simulateFailure    = false,
        int  simulatedStatusCode = 503) =>
        new(
            Options.Create(new FakeOptions
            {
                SimulateFailure     = simulateFailure,
                SimulatedStatusCode = simulatedStatusCode
            }),
            NullLogger<FakeClient>.Instance);

    private static ProviderRequest MakeRequest() => new()
    {
        ProviderKey   = "FAKE",
        CorrelationId = "test-corr",
        Method        = "POST",
        Endpoint      = "https://fake.local/events",
        JsonBody      = "{\"test\":true}"
    };
}

/*
 * ── Pattern for real HTTP clients (Tiger, Opera) ───────────────────────────
 *
 * Use RichardSzalay.MockHttp to intercept HTTP calls without a real server:
 *
 *   var mockHttp = new MockHttpMessageHandler();
 *   mockHttp
 *       .When(HttpMethod.Post, "https://api.tiger-pms.example.com/v1/events")
 *       .Respond(HttpStatusCode.OK, "application/json", "{\"status\":\"ok\"}");
 *
 *   var httpClient = mockHttp.ToHttpClient();
 *   httpClient.BaseAddress = new Uri("https://api.tiger-pms.example.com");
 *
 *   // IHttpClientFactory stub:
 *   var factoryMock = Substitute.For<IHttpClientFactory>();
 *   factoryMock.CreateClient("TIGER").Returns(httpClient);
 *
 *   var tigerClient = new TigerClient(
 *       factoryMock,
 *       Options.Create(new TigerOptions { BaseUrl = "https://api.tiger-pms.example.com", ApiKey = "k" }),
 *       NullLogger<TigerClient>.Instance);
 *
 *   var response = await tigerClient.SendAsync(request);
 *   response.StatusCode.Should().Be(200);
 *   mockHttp.VerifyNoOutstandingExpectation();
 *
 * This pattern keeps tests fast, deterministic, and requires no network.
 */
