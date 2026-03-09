using System.Security;
using System.Text;
using System.Text.Json;
using PmsIntegration.Core.Contracts;

namespace PmsIntegration.Providers.Tiger.Mapping;

/// <summary>
/// Builds the SOAP XML body for the TigerTMS GenericPMS <c>checkIn</c> operation.
/// Pure mapping — no I/O, no side-effects.
/// </summary>
internal static class TigerCheckinSoapBuilder
{
    /// <summary>
    /// Builds the SOAP envelope string for a CheckIn event.
    /// </summary>
    /// <param name="job">The integration job carrying guest data in <see cref="IntegrationJob.Data"/>.</param>
    /// <param name="wsUserKey">Web-service user key from config (wsuserkey element).</param>
    internal static string Build(IntegrationJob job, string wsUserKey)
    {
        var d = job.Data;

        string Get(string key) =>
            d.HasValue && d.Value.TryGetProperty(key, out var el) ? el.GetString() ?? "" : "";

        // ── Inner XML (unencoded) ─────────────────────────────────────────────
        var inner = new StringBuilder();
        inner.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        inner.Append($"<checkinresults resno=\"{Escape(Get("resno"))}\">");
        inner.Append($"<site>{Escape(Get("site"))}</site>");
        inner.Append($"<room>{Escape(Get("room"))}</room>");
        inner.Append($"<title>{Escape(Get("title"))}</title>");
        inner.Append($"<last>{Escape(Get("last"))}</last>");
        inner.Append($"<first>{Escape(Get("first"))}</first>");
        inner.Append($"<guestid>{Escape(Get("guestid"))}</guestid>");
        inner.Append($"<lang>{Escape(Get("lang"))}</lang>");
        inner.Append($"<group>{Escape(Get("group"))}</group>");
        inner.Append($"<vip>{Escape(Get("vip"))}</vip>");
        inner.Append($"<arrival>{Escape(Get("arrival"))}</arrival>");
        inner.Append($"<departure>{Escape(Get("departure"))}</departure>");
        inner.Append($"<tv>{Escape(Get("tv"))}</tv>");
        inner.Append($"<minibar>{Escape(Get("minibar"))}</minibar>");
        inner.Append($"<viewbill>{Escape(Get("viewbill"))}</viewbill>");
        inner.Append($"<expressco>{Escape(Get("expressco"))}</expressco>");
        inner.Append($"<wsuserkey>{Escape(wsUserKey)}</wsuserkey>");
        inner.Append("</checkinresults>");

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
