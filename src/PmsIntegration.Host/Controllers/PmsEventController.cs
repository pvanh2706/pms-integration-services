using Microsoft.AspNetCore.Mvc;
using PmsIntegration.Application.UseCases;
using PmsIntegration.Core.Contracts;

namespace PmsIntegration.Host.Controllers;

[ApiController]
[Route("api/pms")]
public sealed class PmsEventController : ControllerBase
{
    private readonly ReceivePmsEventHandler _handler;

    public PmsEventController(ReceivePmsEventHandler handler)
    {
        _handler = handler;
    }

    /// <summary>
    /// Accepts a PMS event, fans out to provider queues, returns 202 Accepted.
    /// </summary>
    [HttpPost("events")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ReceiveEvent(
        [FromBody] PmsEventEnvelope envelope,
        CancellationToken ct)
    {
        try
        {
            var correlationId = await _handler.HandleAsync(envelope, ct);

            Response.Headers.Append("X-Correlation-Id", correlationId);

            return Accepted(new
            {
                status = "accepted",
                correlationId
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
