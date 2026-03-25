using System.Text.Json.Serialization;

namespace PmsIntegration.Providers.Abstractions.EventData;

/// <summary>
/// Typed representation of the PMS Wakeup and WakeupClear event payloads.
/// Both events share identical fields — only the SOAP inner XML root tag differs.
/// Shared across all providers — parse once, use everywhere.
/// JSON field names match the PMS contract (lowercase snake-case).
/// </summary>
public sealed class WakeupEventData
{
    /// <summary>Mã đặt phòng (chữ + số). Bắt buộc.</summary>
    [JsonPropertyName("resno")]
    public string ReservationNumber { get; set; } = string.Empty;

    /// <summary>Định danh khách sạn/site.</summary>
    [JsonPropertyName("site")]
    public string SiteCode { get; set; } = string.Empty;

    /// <summary>Số phòng.</summary>
    [JsonPropertyName("room")]
    public string RoomNumber { get; set; } = string.Empty;

    /// <summary>Số máy lẻ của phòng.</summary>
    [JsonPropertyName("extension")]
    public string? Extension { get; set; }

    /// <summary>Ngày báo thức — PMS gửi theo format dd/MM/yyyy.</summary>
    [JsonPropertyName("wakeupdate")]
    [JsonConverter(typeof(PmsDateConverter))]
    public DateTime? WakeupDate { get; set; }

    /// <summary>Giờ báo thức — PMS gửi theo format HH:mm:ss.</summary>
    [JsonPropertyName("wakeuptime")]
    [JsonConverter(typeof(PmsTimeConverter))]
    public TimeSpan? WakeupTime { get; set; }
}
