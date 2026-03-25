using System.Text;
using Microsoft.AspNetCore.Mvc;
using PmsIntegration.Providers.Tiger.Incoming;

namespace PmsIntegration.Host.Controllers;

/// <summary>
/// SOAP endpoint that receives incoming messages from TigerTMS.
///
/// TigerTMS calls: POST /tiger/soap
/// with Content-Type: text/xml and a SOAP 1.1 envelope containing one XML string parameter (Msg).
///
/// This endpoint is intentionally outside /api/pms to bypass PmsTokenMiddleware.
/// Authentication is performed via the wsuserkey embedded in the XML payload.
///
/// Response: HTTP 200 with a SOAP envelope wrapping "SUCCESS" or "FAILED – reason".
/// </summary>
[ApiController]
[Route("tiger")]
public sealed class TigerIncomingController : ControllerBase
{
    private readonly TigerIncomingMessageDispatcher _dispatcher;
    private readonly ILogger<TigerIncomingController> _logger;

    public TigerIncomingController(
        TigerIncomingMessageDispatcher dispatcher,
        ILogger<TigerIncomingController> logger)
    {
        _dispatcher = dispatcher;
        _logger     = logger;
    }

    /// <summary>
    /// Receives a SOAP 1.1 request from TigerTMS and returns SUCCESS or FAILED.
    /// Always returns HTTP 200 — error details are embedded in the SOAP response body.
    /// </summary>
    [HttpPost("soap")]
    [Consumes("text/xml", "application/soap+xml")]
    public async Task<ContentResult> ReceiveSoap(CancellationToken ct)
    {
        string rawBody;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true))
            rawBody = await reader.ReadToEndAsync(ct);

        _logger.LogDebug("[TIGER-INCOMING] Received SOAP request ({Bytes} bytes)", rawBody.Length);

        string innerXml;
        try
        {
            innerXml = TigerIncomingSoapParser.ExtractMsgContent(rawBody);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[TIGER-INCOMING] Malformed SOAP envelope: {Error}", ex.Message);
            var failedSoap = TigerIncomingSoapParser.WrapInSoapResponse($"FAILED – Malformed SOAP: {ex.Message}");
            return Content(failedSoap, "text/xml");
        }

        var result = await _dispatcher.HandleAsync(innerXml, ct);

        _logger.LogDebug("[TIGER-INCOMING] Responding with: {Result}", result);

        var soapResponse = TigerIncomingSoapParser.WrapInSoapResponse(result);
        return Content(soapResponse, "text/xml");
    }
}
