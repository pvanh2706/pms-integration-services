using Microsoft.Extensions.Configuration;
using Serilog;

namespace PmsIntegration.Infrastructure.Logging;

/// <summary>
/// Configures Serilog with console sink (Elastic sink to be wired in production).
/// </summary>
public static class SerilogElasticSetup
{
    public static LoggerConfiguration Configure(
        LoggerConfiguration config,
        IConfiguration configuration)
    {
        return config
            .Enrich.FromLogContext()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");

        // Production: add Serilog.Sinks.Elasticsearch and wire here, e.g.:
        // .WriteTo.Elasticsearch(new ElasticsearchSinkOptions(new Uri(esUrl)) { ... })
    }
}
