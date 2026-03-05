namespace PmsIntegration.Core.Abstractions;

/// <summary>
/// Abstraction over wall-clock time to allow deterministic testing.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
