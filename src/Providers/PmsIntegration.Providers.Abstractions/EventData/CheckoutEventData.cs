using System.Text.Json.Serialization;

namespace PmsIntegration.Providers.Abstractions.EventData;

/// <summary>
/// Typed representation of the PMS Checkout event payload.
/// Shared across all providers — parse once, use everywhere.
/// JSON field names match the PMS contract (lowercase snake-case).
/// </summary>
public sealed class CheckoutEventData
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

    /// <summary>ID khách — nên gửi nếu có.</summary>
    [JsonPropertyName("guestid")]
    [JsonConverter(typeof(PmsFlexIntConverter))]
    public int? GuestId { get; set; }

    /// <summary>Danh xưng khách.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Họ khách.</summary>
    [JsonPropertyName("last")]
    public string? LastName { get; set; }

    /// <summary>Tên khách.</summary>
    [JsonPropertyName("first")]
    public string? FirstName { get; set; }
}
