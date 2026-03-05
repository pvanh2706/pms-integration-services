using PmsIntegration.Core.Contracts;

namespace PmsIntegration.Core.Abstractions;

/// <summary>
/// Pure mapping contract: transforms an <see cref="IntegrationJob"/> into
/// the provider-specific JSON payload string.
/// Implementations must be stateless and free of I/O.
/// </summary>
public interface IPmsMapper
{
    string Map(IntegrationJob job);
}
