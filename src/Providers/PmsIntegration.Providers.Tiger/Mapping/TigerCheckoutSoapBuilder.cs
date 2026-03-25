using System.Security;
using System.Text;
using PmsIntegration.Core.Contracts;
using PmsIntegration.Providers.Abstractions.EventData;

namespace PmsIntegration.Providers.Tiger.Mapping;

/// <summary>
/// Builds the SOAP XML body for the TigerTMS GenericPMS <c>checkIn</c> operation
/// carrying a <c>checkoutresults</c> inner payload.
/// Pure mapping — no I/O, no side-effects.
/// </summary>
internal static class TigerCheckoutSoapBuilder
{
    /// <summary>
    /// Builds the SOAP envelope string for a Checkout event.
    /// </summary>
    /// <param name="job">The integration job carrying guest data in <see cref="IntegrationJob.Data"/>.</param>
    /// <param name="wsUserKey">Web-service user key from config (wsuserkey element).</param>
    internal static string Build(IntegrationJob job, string wsUserKey)
    {
        var data = EventDataParser.Parse<CheckoutEventData>(job.Data);

        // ── Inner XML (unencoded) ─────────────────────────────────────────────
        var inner = new StringBuilder();
        inner.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        inner.Append($"<checkoutresults resno=\"{Escape(data.ReservationNumber)}\">");
        inner.Append($"<site>{Escape(data.SiteCode)}</site>");
        inner.Append($"<room>{Escape(data.RoomNumber)}</room>");
        inner.Append($"<title>{Escape(data.Title ?? "")}</title>");
        inner.Append($"<last>{Escape(data.LastName ?? "")}</last>");
        inner.Append($"<first>{Escape(data.FirstName ?? "")}</first>");
        inner.Append($"<guestid>{Escape(data.GuestId?.ToString() ?? "")}</guestid>");
        inner.Append($"<wsuserkey>{Escape(wsUserKey)}</wsuserkey>");
        inner.Append("</checkoutresults>");

        // ── SOAP envelope — XMLString wraps the entity-encoded inner XML ─────
        var soap = new StringBuilder();
        soap.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        soap.Append("<soap:Envelope");
        soap.Append(" xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\"");
        soap.Append(" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"");
        soap.Append(" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\">");
        soap.Append("<soap:Body>");
        soap.Append("<checkIn xmlns=\"http://tigergenericinterface.org/\">");
        soap.Append("<XMLString>");
        soap.Append(Escape(inner.ToString()));   // entity-encode the inner XML string
        soap.Append("</XMLString>");
        soap.Append("</checkIn>");
        soap.Append("</soap:Body>");
        soap.Append("</soap:Envelope>");

        return soap.ToString();
    }

    /// <summary>XML-entity-escapes a string value (safe for use in element text or attribute values).</summary>
    private static string Escape(string value) =>
        SecurityElement.Escape(value) ?? string.Empty;
}
