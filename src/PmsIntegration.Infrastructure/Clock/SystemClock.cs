using PmsIntegration.Core.Abstractions;

namespace PmsIntegration.Infrastructure.Clock;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
