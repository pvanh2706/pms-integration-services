using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Options;
using PmsIntegration.Application.UseCases;
using PmsIntegration.Core.Contracts;
using PmsIntegration.Host.Options;
using PmsIntegration.Infrastructure.Logging.Flow;
using PmsIntegration.Infrastructure.Logging.Masking;

namespace PmsIntegration.Host.Controllers;

[ApiController]
[Route("api/pms-integration-services")]
public sealed class PmsEventController : ControllerBase
{
    private readonly ReceivePmsEventHandler _handler;
    private readonly IApiFlowLogger _flowLogger;
    private readonly PmsSecurityOptions _security;
    private readonly ILogger<PmsEventController> _logger;

    public PmsEventController(
        ReceivePmsEventHandler handler,
        IApiFlowLogger flowLogger,
        IOptions<PmsSecurityOptions> securityOptions,
        ILogger<PmsEventController> logger)
    {
        _handler    = handler;
        _flowLogger = flowLogger;
        _security   = securityOptions.Value;
        _logger     = logger;
    }

    /// <summary>
    /// Accepts a PMS event, fans out to provider queues, returns 202 Accepted.
    /// Writes one API_FLOW document to Elasticsearch via Serilog.
    /// </summary>
    [HttpPost("events")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ReceiveEvent(
        [FromBody] PmsEventEnvelope envelope,
        CancellationToken ct)
    {
        // ── Auth ──────────────────────────────────────────────────────────
        var authResult = ValidateToken();
        if (authResult is not null) return authResult;

        var correlationId = string.IsNullOrWhiteSpace(envelope.CorrelationId)
            ? Guid.NewGuid().ToString()
            : envelope.CorrelationId;
        #region API flow logging
        // ── Start flow ────────────────────────────────────────────────────
        _flowLogger.Start(
            correlationId : correlationId,
            provider      : string.Join(",", envelope.Providers),
            eventType     : envelope.EventType,
            eventId       : envelope.EventId,
            hotelId       : envelope.HotelId,
            requestId     : HttpContext.TraceIdentifier,
            traceId       : HttpContext.TraceIdentifier);

        var stepStart = DateTimeOffset.UtcNow;
        _flowLogger.Step(ApiFlowStep.RequestReceived, stepStart);

        // ── Log client info ───────────────────────────────────────────────
        _flowLogger.SetClientInfo(
            ipAddress  : HttpContext.Connection.RemoteIpAddress?.ToString(),
            port       : HttpContext.Connection.RemotePort,
            requestUrl : HttpContext.Request.GetDisplayUrl());

        // ── Log request payload (masked) ──────────────────────────────────
        var rawBody    = envelope.Data.HasValue ? envelope.Data.Value.GetRawText() : null;
        var maskedBody = rawBody is not null ? PayloadMasker.MaskJson(rawBody) : string.Empty;
        _flowLogger.SetRequestPayload(rawBody, maskedBody, "application/json");
        #endregion
        try
        {
            // ── Delegate to handler (validate + route + publish) ──────────
            var publishStart = DateTimeOffset.UtcNow;
            var returnedCorrelationId = await _handler.HandleAsync(envelope, ct);
            #region API flow logging (continued)
            var publishedAt = DateTimeOffset.UtcNow;
            _flowLogger.Step(ApiFlowStep.QueuePublished, publishedAt);

            _flowLogger.Complete();
            _flowLogger.SetHttpStatusCode(StatusCodes.Status202Accepted);
            _flowLogger.SetResponseBody($"{{\"status\":\"accepted\",\"correlationId\":\"{returnedCorrelationId}\"}}");
            #endregion
            Response.Headers.Append("X-Correlation-Id", returnedCorrelationId);

            return Accepted(new
            {
                status = "accepted",
                correlationId = returnedCorrelationId
            });
        }
        catch (ArgumentException ex)
        {
            #region API flow logging (continued)
            _flowLogger.Fail(
                ApiFlowStep.RequestReceived,
                DateTimeOffset.UtcNow,
                errorCode    : "VALIDATION_ERROR",
                errorMessage : ex.Message);
            _flowLogger.SetHttpStatusCode(StatusCodes.Status400BadRequest);
            _flowLogger.SetResponseBody($"{{\"error\":\"{ex.Message}\"}}");
            #endregion
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            #region API flow logging (continued)
            _flowLogger.Fail(
                ApiFlowStep.QueuePublished,
                DateTimeOffset.UtcNow,
                errorCode    : "UNEXPECTED_ERROR",
                errorMessage : ex.Message);
            _flowLogger.SetHttpStatusCode(StatusCodes.Status500InternalServerError);
            #endregion
            throw;
        }
        finally
        {
            // Always write the document — even on unhandled exceptions.
            _flowLogger.Write();
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Auth helpers (ported from PmsTokenMiddleware)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Validates the fixed PMS token from the request header.
    /// Returns null when auth passes, or an IActionResult (401/503) on failure.
    /// Security properties preserved:
    ///   - Constant-time comparison (timing-attack safe).
    ///   - Single-value header extraction (multi-value join bypass safe).
    ///   - Fail-closed when FixedToken is not configured.
    ///   - Optional replay-window via X-PMS-TIMESTAMP (ISO 8601 UTC).
    /// </summary>
    private IActionResult? ValidateToken()
    {
        if (string.IsNullOrWhiteSpace(_security.FixedToken))
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "auth_not_configured" });

        var headerName = string.IsNullOrWhiteSpace(_security.HeaderName)
            ? "X-PMS-TOKEN"
            : _security.HeaderName;

        // Take only the first value to prevent multi-value header bypass.
        var suppliedToken = Request.Headers[headerName].FirstOrDefault()?.Trim();
        if (suppliedToken is null)
        {
            _logger.LogWarning(
                "PMS token header '{HeaderName}' missing on {Method} {Path}",
                headerName, Request.Method, Request.Path);
            return Unauthorized(new { error = "unauthorized" });
        }

        if (!TokensEqual(suppliedToken, _security.FixedToken))
        {
            _logger.LogWarning(
                "PMS token validation failed on {Method} {Path} from {RemoteIp}",
                Request.Method, Request.Path,
                HttpContext.Connection.RemoteIpAddress);
            return Unauthorized(new { error = "unauthorized" });
        }

        if (_security.ReplayWindowSeconds > 0)
        {
            var tsHeader = Request.Headers["X-PMS-TIMESTAMP"].FirstOrDefault();
            if (!DateTimeOffset.TryParse(tsHeader,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var ts)
                || Math.Abs((DateTimeOffset.UtcNow - ts).TotalSeconds) > _security.ReplayWindowSeconds)
            {
                return Unauthorized(new { error = "timestamp_missing_or_expired" });
            }
        }

        return null; // auth passed
    }

    /// <summary>
    /// Constant-time token comparison via HMACSHA256 to prevent timing oracle.
    /// Both digests are always 32 bytes so FixedTimeEquals always runs to completion.
    /// </summary>
    private static bool TokensEqual(string supplied, string expected)
    {
        var keyMaterial  = Encoding.UTF8.GetBytes(expected);
        var hmacKey      = SHA256.HashData(keyMaterial);
        var suppliedHash = HMACSHA256.HashData(hmacKey, Encoding.UTF8.GetBytes(supplied));
        var expectedHash = HMACSHA256.HashData(hmacKey, keyMaterial);
        return CryptographicOperations.FixedTimeEquals(suppliedHash, expectedHash);
    }
}
