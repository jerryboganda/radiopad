namespace RadioPad.Infrastructure.Integration;

/// <summary>
/// Iter-31 INT-006 — minimal HL7 v2.x parser. Supports the subset RadioPad
/// needs to convert an inbound ORU^R01 / ORM^O01 message into a Draft
/// <see cref="RadioPad.Domain.Entities.Report"/>: MSH header, accession +
/// modality + indication from OBR. PID is captured as an opaque patient
/// reference. Field separators are read from MSH-1 + MSH-2 per HL7 spec, so
/// the parser is robust against vendors that use the standard <c>|^~\&amp;</c>
/// or rare variants.
/// </summary>
public static class Hl7Parser
{
    public sealed record ParsedHl7(
        string MessageType,        // e.g. "ORU^R01" or "ORM^O01"
        string SendingApplication, // MSH-3
        string SendingFacility,    // MSH-4
        string MessageControlId,   // MSH-10
        string ProcessingId,       // MSH-11
        string VersionId,          // MSH-12
        string Accession,          // OBR-3 or OBR-2 fallback
        string Modality,           // OBR-21 or OBR-4 component
        string Indication,         // OBR-31
        string PatientReference,   // PID-3 first repetition, ID part
        char FieldSep,
        char ComponentSep,
        char RepetitionSep,
        char EscapeChar,
        char SubcomponentSep);

    public static ParsedHl7 Parse(string message)
    {
        if (string.IsNullOrEmpty(message))
            throw new ArgumentException("HL7 message is empty.", nameof(message));
        if (!message.StartsWith("MSH"))
            throw new ArgumentException("HL7 message must start with MSH.", nameof(message));
        if (message.Length < 8)
            throw new ArgumentException("HL7 message too short.", nameof(message));

        char fieldSep = message[3];        // MSH-1
        // MSH-2 = encoding chars. message[4..7] = "^~\&" by convention.
        char componentSep = message[4];
        char repetitionSep = message[5];
        char escapeChar = message[6];
        char subcomponentSep = message[7];

        // HL7 segments separated by CR (0x0D); some senders use LF or CRLF.
        var segs = message.Replace("\r\n", "\r").Replace('\n', '\r')
            .Split('\r', StringSplitOptions.RemoveEmptyEntries);

        string sendingApplication = "";
        string sendingFacility = "";
        string messageType = "";
        string messageControlId = "";
        string processingId = "";
        string versionId = "";
        string accession = "";
        string modality = "";
        string indication = "";
        string patientRef = "";

        foreach (var seg in segs)
        {
            var parts = seg.Split(fieldSep);
            var name = parts.Length > 0 ? parts[0] : "";
            switch (name)
            {
                case "MSH":
                    // MSH-1 is the field separator (between MSH and the next field),
                    // so fields after split: parts[0]="MSH", parts[1]="^~\&" (MSH-2),
                    // parts[2]=MSH-3, parts[3]=MSH-4, ... parts[n]=MSH-(n+1).
                    sendingApplication = SafeGet(parts, 2);
                    sendingFacility = SafeGet(parts, 3);
                    messageType = SafeGet(parts, 8);
                    messageControlId = SafeGet(parts, 9);
                    processingId = SafeGet(parts, 10);
                    versionId = SafeGet(parts, 11);
                    break;
                case "PID":
                    // PID-3 = patient identifier list; first repetition, first
                    // component is the ID value.
                    var pid3 = SafeGet(parts, 3);
                    if (!string.IsNullOrEmpty(pid3))
                    {
                        var firstRep = pid3.Split(repetitionSep)[0];
                        var firstComp = firstRep.Split(componentSep)[0];
                        patientRef = firstComp;
                    }
                    break;
                case "OBR":
                    // OBR-2 placer order number, OBR-3 filler order number
                    // (= accession). Try OBR-3 first, fall back to OBR-2.
                    var obr3 = SafeGet(parts, 3);
                    var obr2 = SafeGet(parts, 2);
                    accession = !string.IsNullOrWhiteSpace(obr3) ? FirstComponent(obr3, componentSep) : FirstComponent(obr2, componentSep);
                    // OBR-21 = filler field 2 (vendor-dependent modality slot)
                    var obr21 = SafeGet(parts, 21);
                    var obr4 = SafeGet(parts, 4);
                    modality = !string.IsNullOrWhiteSpace(obr21) ? obr21 : ExtractModalityFromOBR4(obr4, componentSep);
                    indication = SafeGet(parts, 31);
                    if (!string.IsNullOrEmpty(indication))
                        indication = indication.Split(repetitionSep)[0].Split(componentSep)[0];
                    break;
            }
        }

        return new ParsedHl7(
            messageType, sendingApplication, sendingFacility, messageControlId,
            processingId, versionId, accession, modality, indication, patientRef,
            fieldSep, componentSep, repetitionSep, escapeChar, subcomponentSep);
    }

    private static string SafeGet(string[] parts, int idx)
        => idx < parts.Length ? parts[idx] : "";

    private static string FirstComponent(string field, char componentSep)
        => string.IsNullOrEmpty(field) ? "" : field.Split(componentSep)[0];

    /// <summary>
    /// OBR-4 is the universal service identifier (e.g.
    /// <c>71250^CT CHEST^L</c>). Some vendors put a modality code in OBR-4
    /// component 4 (RadioPad-friendly), others use OBR-21. As a last resort
    /// we return the OBR-4 first-component code so callers always have a
    /// non-empty modality string (validation will reject unknown codes).
    /// </summary>
    private static string ExtractModalityFromOBR4(string obr4, char componentSep)
    {
        if (string.IsNullOrEmpty(obr4)) return "";
        var comps = obr4.Split(componentSep);
        // Heuristic: if comp[1] looks like "CT CHEST" pull the first token.
        if (comps.Length >= 2)
        {
            var name = comps[1];
            var firstToken = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (firstToken.Length > 0 && firstToken[0].Length <= 4) return firstToken[0];
        }
        return comps[0];
    }

    /// <summary>
    /// Builds an MSH→ACK reply per HL7 v2.5 (MSA-1 = AA / AE / AR).
    /// </summary>
    public static string BuildAck(ParsedHl7 inbound, string ackCode, string? textMessage = null)
    {
        var f = inbound.FieldSep;
        var encoding = $"{inbound.ComponentSep}{inbound.RepetitionSep}{inbound.EscapeChar}{inbound.SubcomponentSep}";
        var now = DateTimeOffset.UtcNow.UtcDateTime.ToString("yyyyMMddHHmmss");
        var newCtrlId = $"ACK{Guid.NewGuid():N}".Substring(0, 18);
        // MSH | ^~\& | RADIOPAD | RADIOPAD | <theirApp> | <theirFac> | now | | ACK | ctrl | P | 2.5
        var msh = string.Join(f.ToString(), new[]
        {
            "MSH",
            encoding,
            "RADIOPAD",
            "RADIOPAD",
            inbound.SendingApplication,
            inbound.SendingFacility,
            now,
            "",
            "ACK",
            newCtrlId,
            string.IsNullOrEmpty(inbound.ProcessingId) ? "P" : inbound.ProcessingId,
            string.IsNullOrEmpty(inbound.VersionId) ? "2.5" : inbound.VersionId,
        });
        // MSA | <ackCode> | <inboundMsgCtrlId> | <textMessage?>
        var msa = string.Join(f.ToString(), new[]
        {
            "MSA",
            ackCode,
            inbound.MessageControlId,
            textMessage ?? "",
        });
        return msh + "\r" + msa + "\r";
    }
}
