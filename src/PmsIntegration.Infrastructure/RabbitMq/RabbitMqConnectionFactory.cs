using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using PmsIntegration.Infrastructure.Options;

namespace PmsIntegration.Infrastructure.RabbitMq;

/// <summary>
/// Singleton factory that manages the RabbitMQ IConnection lifecycle.
/// Explicit configuration:
///   - AutomaticRecoveryEnabled + TopologyRecoveryEnabled (not relying on defaults).
///   - Heartbeat, NetworkRecoveryInterval, ConnectionTimeout from options.
///   - ConnectionShutdownAsync / recovery events logged for observability.
///   - Stale closed connection is disposed before a new one is created.
/// </summary>
public sealed class RabbitMqConnectionFactory : IAsyncDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqConnectionFactory> _logger;
    private IConnection? _connection;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public RabbitMqConnectionFactory(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqConnectionFactory> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IConnection> GetConnectionAsync(CancellationToken ct = default)
    {
        if (_connection is { IsOpen: true })
            return _connection;

        await _lock.WaitAsync(ct);
        try
        {
            if (_connection is { IsOpen: true })
                return _connection;

            // Dispose stale closed connection before replacing it.
            if (_connection is not null)
            {
                try { await _connection.DisposeAsync(); }
                catch (Exception ex) { _logger.LogWarning(ex, "Error disposing stale RabbitMQ connection."); }
                _connection = null;
            }

            var factory = new ConnectionFactory
            {
                HostName    = _options.Host,
                Port        = _options.Port,
                VirtualHost = _options.VirtualHost,
                UserName    = _options.UserName,
                Password    = _options.Password,

                // Explicit — do not rely on library defaults.
                AutomaticRecoveryEnabled   = true,
                TopologyRecoveryEnabled    = true,
                RequestedHeartbeat         = TimeSpan.FromSeconds(_options.HeartbeatSeconds),
                NetworkRecoveryInterval    = TimeSpan.FromSeconds(_options.NetworkRecoveryIntervalSeconds),
                RequestedConnectionTimeout = TimeSpan.FromSeconds(_options.RequestedConnectionTimeoutSeconds)
            };

            var conn = await factory.CreateConnectionAsync(ct);
            SubscribeConnectionEvents(conn);
            _connection = conn;
            _logger.LogInformation("RabbitMQ connection established to {Host}:{Port}/{VHost}.",
                _options.Host, _options.Port, _options.VirtualHost);
            return _connection;
        }
        finally
        {
            _lock.Release();
        }
    }

    private void SubscribeConnectionEvents(IConnection conn)
    {
        conn.ConnectionShutdownAsync += (_, args) =>
        {
            if (args.Initiator == ShutdownInitiator.Application)
                _logger.LogInformation("RabbitMQ connection closed by application.");
            else
                _logger.LogWarning(
                    "RabbitMQ connection lost (initiator={Initiator}): {ReplyText}",
                    args.Initiator, args.ReplyText);
            return Task.CompletedTask;
        };

        conn.CallbackExceptionAsync += (_, args) =>
        {
            _logger.LogError(args.Exception, "RabbitMQ callback exception.");
            return Task.CompletedTask;
        };

        conn.ConnectionBlockedAsync += (_, args) =>
        {
            _logger.LogWarning("RabbitMQ connection BLOCKED by broker: {Reason}", args.Reason);
            return Task.CompletedTask;
        };

        conn.ConnectionUnblockedAsync += (_, _) =>
        {
            _logger.LogInformation("RabbitMQ connection UNBLOCKED.");
            return Task.CompletedTask;
        };

        // Recovery events are on the IAutorecoveringConnection sub-interface.
        if (conn is IAutorecoveringConnection rc)
        {
            rc.RecoverySucceededAsync += (_, _) =>
            {
                _logger.LogInformation("RabbitMQ connection recovered successfully.");
                return Task.CompletedTask;
            };
            rc.ConnectionRecoveryErrorAsync += (_, args) =>
            {
                _logger.LogError(args.Exception, "RabbitMQ connection recovery failed.");
                return Task.CompletedTask;
            };
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            try { await _connection.DisposeAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Error disposing RabbitMQ connection on shutdown."); }
        }
    }
}
