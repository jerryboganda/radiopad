using System.Text;

namespace RadioPad.Application.Services.Hl7Bridge;

/// <summary>
/// Iter-33 INT-008 — minimal in-memory HL7 v2 ORU^R01 message used as the
/// transport between the Orthanc bridge and RadioPad. The existing
/// <c>Hl7Parser</c> in <c>RadioPad.Infrastructure.Integration</c> only
/// extracts MSH/PID/OBR; the SR ↔ HL7 round-trip needs OBX as well, so this
/// type sits one level up and parses the segments the converters care about
/// (MSH-3..12, OBR-3, OBX-1/-2/-3/-5).
/// </summary>
public sealed record Hl7ObxField(int SetId, string ValueType, string ObservationId, string Value);

public sealed record Hl7Message(
    string MessageType,
    string SendingApplication,
    string SendingFacility,
    string ReceivingApplication,
    string ReceivingFacility,
    string MessageControlId,
    string ProcessingId,
    string VersionId,
    string AccessionNumber,
    string PatientReference,
    IReadOnlyList<Hl7ObxField> Observations)
{
    public const char FieldSep = '|';
    public const char ComponentSep = '^';
    public const char RepetitionSep = '~';
    public const char SegmentTerminator = '\r';

    /// <summary>Parse the subset of ORU^R01 RadioPad's bridge needs.</summary>
    public static Hl7Message Parse(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            throw new ArgumentException("HL7 message is empty.", nameof(raw));
        if (!raw.StartsWith("MSH"))
            throw new ArgumentException("HL7 message must start with MSH.", nameof(raw));

        var segs = raw.Replace("\r\n", "\r").Replace('\n', '\r')
            .Split('\r', StringSplitOptions.RemoveEmptyEntries);

        string messageType = "", sendingApp = "", sendingFac = "", recvApp = "", recvFac = "";
        string ctrlId = "", procId = "P", versionId = "2.5";
        string accession = "", patientRef = "";
        var obx = new List<Hl7ObxField>();

        foreach (var seg in segs)
        {
            var parts = seg.Split(FieldSep);
            switch (parts[0])
            {
                case "MSH":
                    sendingApp = SafeGet(parts, 2);
                    sendingFac = SafeGet(parts, 3);
                    recvApp = SafeGet(parts, 4);
                    recvFac = SafeGet(parts, 5);
                    messageType = SafeGet(parts, 8);
                    ctrlId = SafeGet(parts, 9);
                    procId = SafeGet(parts, 10);
                    versionId = SafeGet(parts, 11);
                    break;
                case "PID":
                    var pid3 = SafeGet(parts, 3);
                    if (!string.IsNullOrEmpty(pid3))
                        patientRef = pid3.Split(RepetitionSep)[0].Split(ComponentSep)[0];
                    break;
                case "ORC":
                    if (string.IsNullOrEmpty(accession))
                        accession = FirstComponent(SafeGet(parts, 3));
                    break;
                case "OBR":
                    var obr3 = SafeGet(parts, 3);
                    var obr2 = SafeGet(parts, 2);
                    accession = !string.IsNullOrWhiteSpace(obr3)
                        ? FirstComponent(obr3)
                        : FirstComponent(obr2);
                    break;
                case "OBX":
                    var setIdRaw = SafeGet(parts, 1);
                    int.TryParse(setIdRaw, out var setId);
                    var vt = SafeGet(parts, 2);
                    var obsId = FirstComponent(SafeGet(parts, 3));
                    var value = SafeGet(parts, 5);
                    obx.Add(new Hl7ObxField(setId == 0 ? obx.Count + 1 : setId, vt, obsId, Unescape(value)));
                    break;
            }
        }

        return new Hl7Message(messageType, sendingApp, sendingFac, recvApp, recvFac,
            ctrlId, string.IsNullOrEmpty(procId) ? "P" : procId,
            string.IsNullOrEmpty(versionId) ? "2.5" : versionId,
            accession, patientRef, obx);
    }

    /// <summary>Render this message as a pipe-delimited HL7 v2.5 ORU^R01 frame.</summary>
    public string Serialize()
    {
        var sb = new StringBuilder();
        var now = DateTimeOffset.UtcNow.UtcDateTime.ToString("yyyyMMddHHmmss");
        sb.Append("MSH|^~\\&|")
          .Append(SendingApplication).Append('|')
          .Append(SendingFacility).Append('|')
          .Append(ReceivingApplication).Append('|')
          .Append(ReceivingFacility).Append('|')
          .Append(now).Append("||")
          .Append(string.IsNullOrEmpty(MessageType) ? "ORU^R01" : MessageType).Append('|')
          .Append(string.IsNullOrEmpty(MessageControlId) ? Guid.NewGuid().ToString("N").Substring(0, 18) : MessageControlId).Append('|')
          .Append(string.IsNullOrEmpty(ProcessingId) ? "P" : ProcessingId).Append('|')
          .Append(string.IsNullOrEmpty(VersionId) ? "2.5" : VersionId)
          .Append(SegmentTerminator);

        sb.Append("PID|1||")
          .Append(string.IsNullOrEmpty(PatientReference) ? "UNKNOWN" : PatientReference)
          .Append("^^^RadioPad^MR")
          .Append(SegmentTerminator);

        // OBR-3 = filler order number (= AccessionNumber). OBR-4 left empty
        // because the SR carries the procedure code, not OBR-4 components.
        sb.Append("OBR|1||").Append(AccessionNumber).Append("|||||||||||||||||||||")
          .Append(now).Append("||RAD||||||F").Append(SegmentTerminator);

        var setId = 1;
        foreach (var o in Observations)
        {
            sb.Append("OBX|").Append(setId).Append('|')
              .Append(string.IsNullOrEmpty(o.ValueType) ? "TX" : o.ValueType).Append('|')
              .Append(string.IsNullOrEmpty(o.ObservationId) ? "FINDING" : o.ObservationId).Append("||")
              .Append(Escape(o.Value)).Append("||||||F").Append(SegmentTerminator);
            setId++;
        }
        return sb.ToString();
    }

    private static string SafeGet(string[] parts, int idx) => idx < parts.Length ? parts[idx] : "";
    private static string FirstComponent(string s)
        => string.IsNullOrEmpty(s) ? "" : s.Split(ComponentSep)[0];

    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Replace("\\", "\\E\\").Replace("|", "\\F\\")
                .Replace("^", "\\S\\").Replace("&", "\\T\\")
                .Replace("~", "\\R\\");
    }

    private static string Unescape(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Replace("\\F\\", "|").Replace("\\S\\", "^")
                .Replace("\\T\\", "&").Replace("\\R\\", "~")
                .Replace("\\E\\", "\\");
    }
}
