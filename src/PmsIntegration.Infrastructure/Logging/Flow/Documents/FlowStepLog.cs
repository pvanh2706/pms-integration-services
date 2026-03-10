namespace PmsIntegration.Infrastructure.Logging.Flow;

/// <summary>
/// Represents one processing step within an API or Provider flow.
/// </summary>
public sealed class FlowStepLog
{
    public string Step { get; set; } = string.Empty;

    /// <summary>"SUCCESS" | "FAILED" | "SKIPPED"</summary>
    public string Status { get; set; } = FlowStatus.Success;

    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset EndedAtUtc   { get; set; }
    public long DurationMs             => (long)(EndedAtUtc - StartedAtUtc).TotalMilliseconds;

    /// <summary>Optional human-readable note or error detail for this step.</summary>
    public string? Message { get; set; }
}
