namespace PmsIntegration.Infrastructure.RabbitMq;

public static class RabbitMqHeaders
{
    public const string CorrelationId = "correlationId";
    public const string HotelId = "hotelId";
    public const string EventId = "eventId";
    public const string EventType = "eventType";
    public const string ProviderKey = "providerKey";
    public const string RetryAttempt = "x-retry-attempt";
    public const string LastErrorCode = "x-last-error-code";
    public const string LastErrorMessage = "x-last-error-message";
}
