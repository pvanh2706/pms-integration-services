using System.Text.Json.Serialization;

namespace PmsIntegration.Providers.Abstractions.EventData;

/// <summary>
/// Typed representation of the PMS Checkin event payload.
/// Shared across all providers — parse once, use everywhere.
/// JSON field names match the PMS contract (lowercase snake-case).
/// </summary>
public sealed class CheckinEventData
{
    /// <summary>Mã đặt phòng (chữ + số). Bắt buộc.</summary>
    [JsonPropertyName("resno")]
    public string ReservationNumber { get; set; } = string.Empty;

    /// <summary>Định danh khách sạn/site. Bắt buộc nếu multi-site.</summary>
    [JsonPropertyName("site")]
    public string SiteCode { get; set; } = string.Empty;

    /// <summary>Số phòng (chữ + số). Bắt buộc.</summary>
    [JsonPropertyName("room")]
    public string RoomNumber { get; set; } = string.Empty;

    /// <summary>Danh xưng khách.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Họ khách.</summary>
    [JsonPropertyName("last")]
    public string? LastName { get; set; }

    /// <summary>Tên khách.</summary>
    [JsonPropertyName("first")]
    public string? FirstName { get; set; }

    /// <summary>ID khách — nên là số duy nhất. Khuyến nghị.</summary>
    [JsonPropertyName("guestid")]
    [JsonConverter(typeof(PmsFlexIntConverter))]
    public int? GuestId { get; set; }

    /// <summary>Mã ngôn ngữ: EA/GE/JP/IT/SP/FR.</summary>
    [JsonPropertyName("lang")]
    public string? LanguageCode { get; set; }

    /// <summary>Tên đoàn.</summary>
    [JsonPropertyName("group")]
    public string? GroupName { get; set; }

    /// <summary>Trạng thái VIP.</summary>
    [JsonPropertyName("vip")]
    public string? VipStatus { get; set; }

    /// <summary>Email khách.</summary>
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    /// <summary>Số điện thoại khách.</summary>
    [JsonPropertyName("mobile")]
    public string? Mobile { get; set; }

    /// <summary>Ngày đến — PMS gửi theo format dd/MM/yyyy.</summary>
    [JsonPropertyName("arrival")]
    [JsonConverter(typeof(PmsDateConverter))]
    public DateTime? ArrivalDate { get; set; }

    /// <summary>Ngày đi — PMS gửi theo format dd/MM/yyyy.</summary>
    [JsonPropertyName("departure")]
    [JsonConverter(typeof(PmsDateConverter))]
    public DateTime? DepartureDate { get; set; }

    /// <summary>Standard/AdultBlocked/Unlimited/None/Unused.</summary>
    [JsonPropertyName("tv")]
    public string? TvSetting { get; set; }

    /// <summary>Standard/Locked/Unused.</summary>
    [JsonPropertyName("minibar")]
    public string? MinibarSetting { get; set; }

    /// <summary>Cho phép khách xem hóa đơn.</summary>
    [JsonPropertyName("viewbill")]
    [JsonConverter(typeof(PmsFlexBoolConverter))]
    public bool? AllowViewBill { get; set; }

    /// <summary>Cho phép Express Check-out.</summary>
    [JsonPropertyName("expressco")]
    [JsonConverter(typeof(PmsFlexBoolConverter))]
    public bool? AllowExpressCO { get; set; }
}
