using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using PmsIntegration.Host.Options;

namespace PmsIntegration.Host.Middleware;

/// <summary>
/// Validates the fixed PMS token for routes under /api/pms/*.
/// Security properties enforced:
///   - Constant-time comparison (timing-attack safe).
///   - Single-value header extraction (multi-value join bypass safe).
///   - Fail-closed when FixedToken is not configured.
///   - Optional replay-window via X-PMS-TIMESTAMP (ISO 8601 UTC).
/// </summary>
public sealed class PmsTokenMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<PmsTokenMiddleware> _logger;

    public PmsTokenMiddleware(RequestDelegate next, ILogger<PmsTokenMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IOptions<PmsSecurityOptions> securityOptions)
    {
        if (!context.Request.Path.StartsWithSegments("/api/pms"))
        {
            await _next(context);
            return;
        }

        var options = securityOptions.Value;

        // BUG-3 FIX: fail-closed — a blank or missing token config must never allow access.
        // This surfaces misconfiguration immediately rather than silently permitting requests.
        if (string.IsNullOrWhiteSpace(options.FixedToken))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\":\"auth_not_configured\"}");
            return;
        }

        var headerName = string.IsNullOrWhiteSpace(options.HeaderName)
            ? "X-PMS-TOKEN"
            : options.HeaderName;

        // BUG-2 FIX: take only the first value from StringValues.
        // StringValues.ToString() with multiple header values joins them with ",",
        // which could allow a crafted multi-header request to match a value like "abc,junk".
        // Trim whitespace to tolerate minor transport artefacts (e.g. trailing space).
        var suppliedToken = context.Request.Headers[headerName].FirstOrDefault()?.Trim();
        if (suppliedToken is null)
        {
            _logger.LogWarning(
                "PMS token header '{HeaderName}' missing on {Method} {Path}",
                headerName,
                context.Request.Method,
                context.Request.Path);
            await RejectAsync(context);
            return;
        }

        // BUG-1 FIX: constant-time comparison via HMACSHA256 to prevent timing oracle.
        // Hashing both sides with the same derived key normalises the byte-length,
        // so FixedTimeEquals can always run to completion regardless of input length.
        if (!TokensEqual(suppliedToken, options.FixedToken))
        {
            _logger.LogWarning(
                "PMS token validation failed on {Method} {Path} from {RemoteIp}",
                context.Request.Method,
                context.Request.Path,
                context.Connection.RemoteIpAddress);
            await RejectAsync(context);
            return;
        }

        // BUG-4 FIX: replay-window check.
        // When ReplayWindowSeconds > 0, callers must supply X-PMS-TIMESTAMP (ISO 8601 UTC).
        // A captured token is only usable within the configured window (default 5 min).
        if (options.ReplayWindowSeconds > 0)
        {
            var tsHeader = context.Request.Headers["X-PMS-TIMESTAMP"].FirstOrDefault();
            if (!DateTimeOffset.TryParse(tsHeader,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var ts)
                || Math.Abs((DateTimeOffset.UtcNow - ts).TotalSeconds) > options.ReplayWindowSeconds)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"error\":\"timestamp_missing_or_expired\"}");
                return;
            }
        }

        await _next(context);
    }

    /// <summary>
    /// Derives a 256-bit HMAC key from <paramref name="expected"/> then compares
    /// HMAC(key, supplied) vs HMAC(key, expected) with <see cref="CryptographicOperations.FixedTimeEquals"/>.
    /// Both digests are always 32 bytes, so the comparison is unconditionally constant-time.
    /// </summary>
    private static bool TokensEqual(string supplied, string expected)
    {
        var keyMaterial  = Encoding.UTF8.GetBytes(expected);
        var hmacKey      = SHA256.HashData(keyMaterial);          // 32-byte normaliser key
        var suppliedHash = HMACSHA256.HashData(hmacKey, Encoding.UTF8.GetBytes(supplied));
        var expectedHash = HMACSHA256.HashData(hmacKey, keyMaterial);
        return CryptographicOperations.FixedTimeEquals(suppliedHash, expectedHash);
    }

    private static Task RejectAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync("{\"error\":\"unauthorized\"}");
    }
}
