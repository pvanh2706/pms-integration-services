using Microsoft.Extensions.Configuration;
using PmsIntegration.Infrastructure.Options;
using Serilog;
using Serilog.Sinks.Elasticsearch;

namespace PmsIntegration.Infrastructure.Logging;

/// <summary>
/// Configures Serilog with console sink and, when <c>Elastic:Enabled = true</c>
/// in appsettings, an additional Elasticsearch sink.
///
/// appsettings example:
/// <code>
/// "Elastic": {
///   "Enabled": true,
///   "Uri": "http://my-elastic:9200",
///   "IndexPrefix": "pms-integration-services-logs"
/// }
/// </code>
/// </summary>
public static class SerilogElasticSetup
{
    public static LoggerConfiguration Configure(
        LoggerConfiguration config,
        IConfiguration configuration)
    {
        var lc = config
            .Enrich.FromLogContext()
            // Console: regular app logs only — flow documents (API_FLOW / PROVIDER_FLOW)
            // are excluded here; they go to Elasticsearch only.
            .WriteTo.Logger(sub => sub
                .Filter.ByExcluding(e => e.Properties.ContainsKey("FlowLog"))
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"));

        var opts = configuration.GetSection("Elastic").Get<ElasticOptions>();
        if (opts is { Enabled: true } && !string.IsNullOrWhiteSpace(opts.Uri))
        {
            // Elasticsearch: flow documents only (API_FLOW + PROVIDER_FLOW).
            // The "FlowLog" property is added exclusively by ApiFlowLogger.Write()
            // and ProviderFlowLogger.Write() via the {@FlowLog} destructuring token.
            lc.WriteTo.Logger(sub => sub
                .Filter.ByIncludingOnly(e => e.Properties.ContainsKey("FlowLog"))
                .WriteTo.Elasticsearch(new ElasticsearchSinkOptions(new Uri(opts.Uri))
                {
                    AutoRegisterTemplate = true,
                    IndexFormat          = $"{opts.IndexPrefix}",
                    // Prevent Elasticsearch write failures from crashing the process
                    EmitEventFailure     = EmitEventFailureHandling.WriteToSelfLog
                }));
        }

        return lc;
    }
}
