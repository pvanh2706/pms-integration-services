namespace PmsIntegration.Infrastructure.Options;

public sealed class QueueOptions
{
    public int RetryDelaySeconds { get; set; } = 30;
    public int MaxRetryAttempts { get; set; } = 3;
    public Dictionary<string, string> ProviderQueues { get; set; } = new();
}
