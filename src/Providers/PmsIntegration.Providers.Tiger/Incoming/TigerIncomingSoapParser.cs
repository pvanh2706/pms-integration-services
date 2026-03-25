using System.Xml.Linq;

namespace PmsIntegration.Providers.Tiger.Incoming;

/// <summary>
/// Stateless helpers for reading incoming TigerTMS SOAP envelopes and building SOAP responses.
///
/// TigerTMS calls our endpoint with a SOAP 1.1 envelope wrapping a string parameter named "Msg".
/// The Msg content is the inner XML payload (typically HTML-entity-escaped).
/// </summary>
public static class TigerIncomingSoapParser
{
    private static readonly XNamespace SoapNs  = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace TigerNs = "http://CONNECTED GUESTSgenericinterface.org/";

    /// <summary>
    /// Extracts the string content of the &lt;Msg&gt; parameter from a SOAP 1.1 envelope.
    /// Handles both escaped XML (most common) and raw XML child elements inside &lt;Msg&gt;.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the envelope structure is unexpected.</exception>
    public static string ExtractMsgContent(string soapEnvelope)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(soapEnvelope);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Cannot parse SOAP envelope as XML: {ex.Message}", ex);
        }

        var msgElement = doc.Root
            ?.Element(SoapNs + "Body")
            ?.Element(TigerNs + "SendMessageToExternalInterface")
            ?.Element(TigerNs + "Msg");

        if (msgElement is null)
            throw new InvalidOperationException(
                "SOAP Body does not contain a valid SendMessageToExternalInterface/Msg element.");

        // Case 1: Msg contains escaped XML as text (standard ASMX behaviour)
        //         XElement.Value automatically decodes HTML entities, giving raw XML string.
        var textContent = msgElement.Value.Trim();
        if (textContent.StartsWith("<", StringComparison.Ordinal))
            return textContent;

        // Case 2: Msg contains a direct XML child element (non-ASMX senders)
        var firstChild = msgElement.Elements().FirstOrDefault();
        if (firstChild is not null)
            return firstChild.ToString(SaveOptions.DisableFormatting);

        // Case 3: truly empty — return as-is; dispatcher will handle gracefully
        return textContent;
    }

    /// <summary>
    /// Wraps a plain-string result ("SUCCESS" or "FAILED – reason") in a SOAP 1.1 response envelope.
    /// </summary>
    public static string WrapInSoapResponse(string result)
    {
        // Escape result string so it is safe as XML text content.
        var safeResult = new System.Xml.Linq.XText(result).ToString();

        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"
                           xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                           xmlns:xsd="http://www.w3.org/2001/XMLSchema">
              <soap:Body>
                <SendMessageToExternalInterfaceResponse xmlns="http://CONNECTED GUESTSgenericinterface.org/">
                  <SendMessageToExternalInterfaceResult>{safeResult}</SendMessageToExternalInterfaceResult>
                </SendMessageToExternalInterfaceResponse>
              </soap:Body>
            </soap:Envelope>
            """;
    }
}
