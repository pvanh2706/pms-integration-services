namespace PmsIntegration.Providers.Fake;

public sealed class FakeOptions
{
    public string BaseUrl { get; set; } = "https://fake.provider.local";
    public string ApiKey { get; set; } = "fake-api-key";
    public int TimeoutSeconds { get; set; } = 10;
    public bool SimulateFailure { get; set; } = false;
    public int SimulatedStatusCode { get; set; } = 200;
}
