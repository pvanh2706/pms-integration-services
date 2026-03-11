namespace PmsIntegration.Infrastructure.Options;

public sealed class ElasticOptions
{
    public bool   Enabled     { get; init; }
    public string Uri         { get; init; } = string.Empty;
    public string IndexPrefix { get; init; } = "pms-integration-services-logs";
}
