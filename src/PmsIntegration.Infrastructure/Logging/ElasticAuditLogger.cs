using Microsoft.Extensions.Logging;
using PmsIntegration.Core.Abstractions;

namespace PmsIntegration.Infrastructure.Logging;

/// <summary>
/// Structured audit logger backed by Microsoft.Extensions.Logging / Serilog.
/// Format: AUDIT {Action} {@Data}
/// </summary>
public sealed class ElasticAuditLogger : IAuditLogger
{
    private readonly ILogger<ElasticAuditLogger> _logger;

    public ElasticAuditLogger(ILogger<ElasticAuditLogger> logger)
    {
        _logger = logger;
    }

    public void Log(string action, object data)
    {
        _logger.LogInformation("AUDIT {Action} {@Data}", action, data);
    }
}
