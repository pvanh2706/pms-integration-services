namespace PmsIntegration.Providers.Tiger.Incoming.Models;

public enum ChargeRecordType
{
    CallRecord,
    Internet,
    Minibar,
    Video,
    Unknown
}

/// <summary>
/// Parsed representation of a TigerTMS &lt;chargerecord&gt; message.
/// Covers all four charge types: callrecord, internet, minibar, video.
/// </summary>
public sealed class TigerChargeRecord
{
    /// <summary>Tiger XML: resno attribute. Always blank per spec, reserved for future use.</summary>
    public string ReservationNumber { get; init; } = string.Empty;

    /// <summary>Tiger XML: type attribute — callrecord, internet, minibar, video.</summary>
    public ChargeRecordType Type { get; init; }

    /// <summary>Tiger XML: &lt;site&gt;</summary>
    public string SiteCode { get; init; } = string.Empty;

    /// <summary>Tiger XML: &lt;room&gt;</summary>
    public string RoomNumber { get; init; } = string.Empty;

    /// <summary>Tiger XML: &lt;datetime&gt; — format dd/MM/yyyy HH:mm:ss</summary>
    public DateTime? DateTime { get; init; }

    /// <summary>Tiger XML: &lt;dialled&gt; — phone number dialled (callrecord only)</summary>
    public string? Dialled { get; init; }

    /// <summary>Tiger XML: &lt;duration&gt; — format HH:mm:ss</summary>
    public string? Duration { get; init; }

    /// <summary>Tiger XML: &lt;calltype&gt; — O=Outgoing, I=Incoming</summary>
    public string? CallType { get; init; }

    /// <summary>Tiger XML: &lt;charge&gt; — major.minor currency</summary>
    public decimal Charge { get; init; }

    /// <summary>Tiger XML: &lt;callcategory&gt; — F=Free, L=Local, N=National, I=International, M=Mobile</summary>
    public string? CallCategory { get; init; }

    /// <summary>Tiger XML: &lt;wsuserkey&gt; — validation key</summary>
    public string WsUserKey { get; init; } = string.Empty;
}
