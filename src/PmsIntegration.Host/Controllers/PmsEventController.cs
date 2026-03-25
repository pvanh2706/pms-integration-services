using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Extensions;
using PmsIntegration.Application.UseCases;
using PmsIntegration.Core.Contracts;
using PmsIntegration.Infrastructure.Logging.Flow;
using PmsIntegration.Infrastructure.Logging.Masking;

namespace PmsIntegration.Host.Controllers;

[ApiController]
[Route("api/pms")]
public sealed class PmsEventController : ControllerBase
{
    private readonly ReceivePmsEventHandler _handler;
    private readonly IApiFlowLogger _flowLogger;

    public PmsEventController(ReceivePmsEventHandler handler, IApiFlowLogger flowLogger)
    {
        _handler    = handler;
        _flowLogger = flowLogger;
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
}
