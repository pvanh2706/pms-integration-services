namespace PmsIntegration.Infrastructure.Options;

public sealed class RabbitMqOptions
{
    public List<string> Nodes { get; set; } = [];
    public int Port { get; set; } = 5672;
    public string VirtualHost { get; set; } = "";
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";

    /// <summary>AMQP heartbeat interval. 0 = disabled (not recommended).</summary>
    public int HeartbeatSeconds { get; set; } = 30;

    /// <summary>How long to wait between automatic recovery attempts.</summary>
    public int NetworkRecoveryIntervalSeconds { get; set; } = 10;

    /// <summary>TCP connection attempt timeout.</summary>
    public int RequestedConnectionTimeoutSeconds { get; set; } = 30;

    /// <summary>Delay before a consumer background service retries after total connection loss.</summary>
    public int ConsumerReconnectDelaySeconds { get; set; } = 10;
}
