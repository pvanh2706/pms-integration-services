using System.Text.Json;
using System.Text.Json.Serialization;

namespace PmsIntegration.Providers.Abstractions.EventData;

/// <summary>
/// Deserializes <see cref="JsonElement"/> PMS event payloads into typed models.
/// All providers use this single entry point — parse logic lives here, not scattered across mappers.
/// </summary>
public static class EventDataParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Parses <paramref name="data"/> into <typeparamref name="T"/>.
    /// Returns a default-constructed instance when <paramref name="data"/> is null.
    /// </summary>
    public static T Parse<T>(JsonElement? data) where T : new()
    {
        if (!data.HasValue)
            return new T();

        return JsonSerializer.Deserialize<T>(data.Value.GetRawText(), Options) ?? new T();
    }
}

// ── Internal converters ──────────────────────────────────────────────────────
// These are part of the PMS contract (date format, loose bool/int encoding)
// and are intentionally kept internal — callers work with typed models only.

/// <summary>Parses PMS date strings in dd/MM/yyyy format.</summary>
internal sealed class PmsDateConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        if (string.IsNullOrWhiteSpace(s)) return null;
        return DateTime.TryParseExact(
            s, "dd/MM/yyyy",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out var d) ? d : null;
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
        => writer.WriteStringValue(value?.ToString("dd/MM/yyyy"));
}

/// <summary>Accepts JSON boolean or string "True"/"False" (PMS may send either).</summary>
internal sealed class PmsFlexBoolConverter : JsonConverter<bool?>
{
    public override bool? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.True)  return true;
        if (reader.TokenType == JsonTokenType.False) return false;
        if (reader.TokenType == JsonTokenType.Null)  return null;
        if (reader.TokenType == JsonTokenType.String)
            return bool.TryParse(reader.GetString(), out var b) ? b : null;
        return null;
    }

    public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
    {
        if (value.HasValue) writer.WriteBooleanValue(value.Value);
        else writer.WriteNullValue();
    }
}

/// <summary>Parses PMS time strings in HH:mm:ss format.</summary>
internal sealed class PmsTimeConverter : JsonConverter<TimeSpan?>
{
    public override TimeSpan? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        if (string.IsNullOrWhiteSpace(s)) return null;
        return TimeSpan.TryParseExact(s, @"hh\:mm\:ss",
            System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : null;
    }

    public override void Write(Utf8JsonWriter writer, TimeSpan? value, JsonSerializerOptions options)
        => writer.WriteStringValue(value?.ToString(@"hh\:mm\:ss"));
}

/// <summary>Accepts JSON number or string representation of an integer.</summary>
internal sealed class PmsFlexIntConverter : JsonConverter<int?>
{
    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number) return reader.GetInt32();
        if (reader.TokenType == JsonTokenType.Null)   return null;
        if (reader.TokenType == JsonTokenType.String)
            return int.TryParse(reader.GetString(), out var i) ? i : null;
        return null;
    }

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        if (value.HasValue) writer.WriteNumberValue(value.Value);
        else writer.WriteNullValue();
    }
}
