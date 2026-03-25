using System.Globalization;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PmsIntegration.Providers.Tiger.Incoming.Models;

namespace PmsIntegration.Providers.Tiger.Incoming;

/// <summary>
/// Validates, routes, and handles incoming XML messages sent by TigerTMS.
///
/// Per TigerTMS spec: any unrecognised/unimplemented message type MUST return "SUCCESS"
/// to prevent timeouts and retransmissions on the Tiger side.
/// </summary>
public sealed class TigerIncomingMessageDispatcher
{
    private readonly TigerOptions _options;
    private readonly ILogger<TigerIncomingMessageDispatcher> _logger;

    public TigerIncomingMessageDispatcher(
        IOptions<TigerOptions> options,
        ILogger<TigerIncomingMessageDispatcher> logger)
    {
        _options = options.Value;
        _logger  = logger;
    }

    /// <summary>
    /// Parses and handles an incoming Tiger XML message.
    /// Returns "SUCCESS" or "FAILED – &lt;reason&gt;".
    /// Never throws — all exceptions are caught and converted to FAILED responses.
    /// </summary>
    public Task<string> HandleAsync(string innerXml, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(innerXml))
            return Task.FromResult("FAILED – Empty message body.");

        XElement root;
        try
        {
            root = XElement.Parse(innerXml);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[TIGER-INCOMING] Failed to parse inner XML: {Error}", ex.Message);
            return Task.FromResult($"FAILED – Invalid XML: {ex.Message}");
        }

        // Validate wsuserkey when configured.
        // Skip validation when WsUserKey is blank (useful for dev/test environments).
        var wsUserKey = root.Element("wsuserkey")?.Value ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(_options.WsUserKey)
            && !string.Equals(wsUserKey, _options.WsUserKey, StringComparison.Ordinal))
        {
            _logger.LogWarning("[TIGER-INCOMING] Invalid wsuserkey on message type '{Type}'",
                root.Name.LocalName);
            return Task.FromResult("FAILED – Invalid wsuserkey.");
        }

        var messageType = root.Name.LocalName.ToLowerInvariant();

        return messageType switch
        {
            "chargerecord" => HandleChargeRecord(root),
            _ => AcknowledgeUnimplemented(messageType)
        };
    }

    // ── Handlers ─────────────────────────────────────────────────────────

    private Task<string> HandleChargeRecord(XElement root)
    {
        var room = root.Element("room")?.Value?.Trim();
        if (string.IsNullOrEmpty(room))
            return Task.FromResult("FAILED – Invalid room number.");

        var chargeRecord = ParseChargeRecord(root);

        _logger.LogInformation(
            "[TIGER-INCOMING] ChargeRecord type={Type} site={Site} room={Room} charge={Charge} datetime={DateTime}",
            chargeRecord.Type,
            chargeRecord.SiteCode,
            chargeRecord.RoomNumber,
            chargeRecord.Charge,
            chargeRecord.DateTime?.ToString("o"));

        // TODO: forward chargeRecord to internal processing queue when downstream is ready.

        return Task.FromResult("SUCCESS");
    }

    private Task<string> AcknowledgeUnimplemented(string messageType)
    {
        _logger.LogInformation(
            "[TIGER-INCOMING] Received unimplemented message type '{Type}' — acknowledged with SUCCESS",
            messageType);
        return Task.FromResult("SUCCESS");
    }

    // ── Parsers ───────────────────────────────────────────────────────────

    private static TigerChargeRecord ParseChargeRecord(XElement root)
    {
        var typeStr = root.Attribute("type")?.Value?.ToLowerInvariant() ?? string.Empty;
        var type = typeStr switch
        {
            "callrecord" => ChargeRecordType.CallRecord,
            "internet"   => ChargeRecordType.Internet,
            "minibar"    => ChargeRecordType.Minibar,
            "video"      => ChargeRecordType.Video,
            _            => ChargeRecordType.Unknown
        };

        DateTime? dateTime = null;
        var rawDateTime = root.Element("datetime")?.Value;
        if (!string.IsNullOrWhiteSpace(rawDateTime)
            && DateTime.TryParseExact(
                rawDateTime,
                "dd/MM/yyyy HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedDateTime))
        {
            dateTime = parsedDateTime;
        }

        _ = decimal.TryParse(
            root.Element("charge")?.Value ?? "0",
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var charge);

        return new TigerChargeRecord
        {
            ReservationNumber = root.Attribute("resno")?.Value ?? string.Empty,
            Type              = type,
            SiteCode          = root.Element("site")?.Value ?? string.Empty,
            RoomNumber        = root.Element("room")?.Value?.Trim() ?? string.Empty,
            DateTime          = dateTime,
            Dialled           = root.Element("dialled")?.Value,
            Duration          = root.Element("duration")?.Value,
            CallType          = root.Element("calltype")?.Value,
            Charge            = charge,
            CallCategory      = root.Element("callcategory")?.Value,
            WsUserKey         = root.Element("wsuserkey")?.Value ?? string.Empty
        };
    }
}
