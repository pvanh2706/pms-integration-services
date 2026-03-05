namespace PmsIntegration.Core.Abstractions;

/// <summary>
/// Writes structured audit events to Elastic via Serilog.
/// Fixed audit actions: pms.received, job.enqueued, job.processing,
/// job.success, job.failed, job.dlq, job.duplicate_ignored.
/// </summary>
public interface IAuditLogger
{
    void Log(string action, object data);
}
