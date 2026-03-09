using System.Text.RegularExpressions;

namespace PmsIntegration.Infrastructure.Logging.Masking;

/// <summary>
/// Pure static helpers for masking sensitive fields before writing to log.
/// 
/// Approach:
///   - For XML (Tiger SOAP): regex-replace sensitive element values.
///   - For JSON: regex-replace sensitive key-value pairs.
///   - For plain strings: replace known tokens/keywords.
///   - Truncation helper caps strings to a safe max length.
/// 
/// This class is intentionally static — it is pure, side-effect-free,
/// and safe to call from any thread without shared state.
/// </summary>
public static partial class PayloadMasker
{
    /// <summary>
    /// Maximum character count before truncation. 10 000 chars ≈ ~10 KB.
    /// </summary>
    public const int DefaultMaxLength = 10_000;

    private const string MaskValue = "***";

    // ── Pre-compiled regexes ──────────────────────────────────────────────

    /// <summary>Masks XML elements whose content is sensitive, e.g. &lt;wsuserkey&gt;ABC&lt;/wsuserkey&gt;.</summary>
    [GeneratedRegex(
        @"(<(?:wsuserkey|password|token|email|mobile)>)([^<]*)(</(?:wsuserkey|password|token|email|mobile)>)",
        RegexOptions.IgnoreCase)]
    private static partial Regex XmlSensitiveFieldRegex();

    /// <summary>
    /// Masks XML-escaped inner payload (&amp;lt;wsuserkey&amp;gt; … variant).
    /// Tiger wraps inner XML as escaped content inside &lt;XMLString&gt;.
    /// </summary>
    [GeneratedRegex(
        @"(&lt;(?:wsuserkey|password|token|email|mobile)&gt;)([^&]*)(&lt;/(?:wsuserkey|password|token|email|mobile)&gt;)",
        RegexOptions.IgnoreCase)]
    private static partial Regex XmlEscapedSensitiveFieldRegex();

    /// <summary>Masks JSON string values for sensitive keys.</summary>
    [GeneratedRegex(
        @"""(password|token|email|mobile|authorization|secret|apikey|api_key)""\s*:\s*""([^""]*)""",
        RegexOptions.IgnoreCase)]
    private static partial Regex JsonSensitiveFieldRegex();

    /// <summary>Masks Authorization header values in serialised header dictionaries.</summary>
    [GeneratedRegex(
        @"(Authorization\s*[:=]\s*)([^\s,""]+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex AuthorizationHeaderRegex();

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Masks all sensitive fields in a Tiger SOAP/XML body.
    /// Handles both raw and XML-escaped variants of the inner payload.
    /// Returns the masked string, truncated to <see cref="DefaultMaxLength"/>.
    /// </summary>
    public static string MaskXml(string? xml)
    {
        if (string.IsNullOrEmpty(xml)) return string.Empty;

        var result = XmlSensitiveFieldRegex()
            .Replace(xml, m => $"{m.Groups[1].Value}{MaskValue}{m.Groups[3].Value}");

        result = XmlEscapedSensitiveFieldRegex()
            .Replace(result, m => $"{m.Groups[1].Value}{MaskValue}{m.Groups[3].Value}");

        return Truncate(result);
    }

    /// <summary>
    /// Masks all sensitive fields in a JSON body.
    /// Returns the masked string, truncated to <see cref="DefaultMaxLength"/>.
    /// </summary>
    public static string MaskJson(string? json)
    {
        if (string.IsNullOrEmpty(json)) return string.Empty;

        var result = JsonSensitiveFieldRegex()
            .Replace(json, m => $@"""{m.Groups[1].Value}"": ""{MaskValue}""");

        result = AuthorizationHeaderRegex()
            .Replace(result, m => $"{m.Groups[1].Value}{MaskValue}");

        return Truncate(result);
    }

    /// <summary>
    /// Auto-detects XML vs JSON and masks accordingly.
    /// Falls back to <see cref="MaskJson"/> for unrecognised formats.
    /// </summary>
    public static string Mask(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;

        var trimmed = body.TrimStart();
        return trimmed.StartsWith('<') ? MaskXml(body) : MaskJson(body);
    }

    /// <summary>
    /// Truncates <paramref name="value"/> to at most <paramref name="maxLength"/> characters.
    /// Appends "[TRUNCATED]" indicator when the string is cut.
    /// </summary>
    public static string Truncate(string? value, int maxLength = DefaultMaxLength)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.Length <= maxLength)   return value;

        const string suffix = "... [TRUNCATED]";
        return string.Concat(value.AsSpan(0, maxLength - suffix.Length), suffix);
    }
}
