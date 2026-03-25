using System.Security;
using System.Text;
using PmsIntegration.Core.Contracts;
using PmsIntegration.Providers.Abstractions.EventData;

namespace PmsIntegration.Providers.Tiger.Mapping;

/// <summary>
/// Builds the SOAP XML body for the TigerTMS GenericPMS
/// <c>externalInterfaceMessageIn</c> operation for Wakeup and WakeupClear events.
/// Both events share the same fields and SOAP operation — only the inner XML root tag differs.
/// Pure mapping — no I/O, no side-effects.
/// </summary>
internal static class TigerWakeupSoapBuilder
{
    /// <param name="job">The integration job carrying wakeup data.</param>
    /// <param name="wsUserKey">Web-service user key from config.</param>
    /// <param name="isWakeupClear">True builds wakeupClear root tag, false builds wakeup root tag.</param>
    internal static string Build(IntegrationJob job, string wsUserKey, bool isWakeupClear = false)
    {
        var data    = EventDataParser.Parse<WakeupEventData>(job.Data);
        var rootTag = isWakeupClear ? "wakeupclearresults" : "wakeupsetresults";

        // -- Inner XML (unencoded) -------------------------------------------------
        var inner = new StringBuilder();
        inner.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        inner.Append($"<{rootTag} resno=\"{Escape(data.ReservationNumber)}\">");
        inner.Append($"<site>{Escape(data.SiteCode)}</site>");
        inner.Append($"<room>{Escape(data.RoomNumber)}</room>");
        inner.Append($"<extension>{Escape(data.Extension ?? "")}</extension>");
        inner.Append($"<wakeupdate>{Escape(data.WakeupDate?.ToString("dd/MM/yyyy") ?? "")}</wakeupdate>");
        inner.Append($"<wakeuptime>{Escape(data.WakeupTime?.ToString(@"hh\:mm\:ss") ?? "")}</wakeuptime>");
        inner.Append($"<wsuserkey>{Escape(wsUserKey)}</wsuserkey>");
        inner.Append($"</{rootTag}>");

        // -- SOAP envelope — XMLString wraps the entity-encoded inner XML ----------
        var soap = new StringBuilder();
        soap.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        soap.Append("<soap:Envelope");
        soap.Append(" xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\"");
        soap.Append(" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"");
        soap.Append(" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\">");
        soap.Append("<soap:Body>");
        soap.Append("<externalInterfaceMessageIn xmlns=\"http://tigergenericinterface.org/\">");
        soap.Append("<XMLString>");
        soap.Append(Escape(inner.ToString()));
        soap.Append("</XMLString>");
        soap.Append("</externalInterfaceMessageIn>");
        soap.Append("</soap:Body>");
        soap.Append("</soap:Envelope>");

        return soap.ToString();
    }

    private static string Escape(string value) =>
        SecurityElement.Escape(value) ?? string.Empty;
}
