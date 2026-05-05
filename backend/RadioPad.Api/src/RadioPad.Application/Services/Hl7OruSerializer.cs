using System.Text;
using RadioPad.Domain.Entities;

namespace RadioPad.Application.Services;

/// <summary>
/// PRD §19.1 / Beta — HL7 v2.5 ORU^R01 serializer. Produces a minimal, valid
/// pipe-delimited message (MSH | PID | OBR | OBX) suitable for transport into
/// a radiology RIS that consumes ORU result messages. The implementation
/// purposefully avoids a third-party HL7 stack: ORU^R01 has a stable shape and
/// the value RadioPad provides at this layer is the deterministic mapping
/// from <see cref="Report"/> + <see cref="Tenant"/> to a parseable message.
///
/// The body of the report is emitted as a single TX OBX repeating the report
/// narrative, segmented by section, with carriage return as the segment
/// terminator (HL7 default). PHI is intentionally limited to what already
/// lives on the report (accession + patient reference); the full report text
/// is the same text the radiologist already authored.
/// </summary>
public static class Hl7OruSerializer
{
    private const char SegmentTerminator = '\r';

    public static string Serialize(Report report, Tenant tenant)
    {
        var now = DateTimeOffset.UtcNow.UtcDateTime.ToString("yyyyMMddHHmmss");
        var msgId = $"RP{Guid.NewGuid():N}".Substring(0, 20);
        var sendingApp = $"RADIOPAD^{tenant.Slug.ToUpperInvariant()}";
        var accession = Escape(report.Study.AccessionNumber);
        var modality = Escape(report.Study.Modality ?? "");
        var bodyPart = Escape(report.Study.BodyPart ?? "");
        var patientRef = Escape(report.Study.PatientReference ?? "UNKNOWN");

        var sb = new StringBuilder();

        // MSH — Message Header. Field separator | and encoding chars ^~\&
        sb.Append("MSH|^~\\&|").Append(sendingApp).Append("|RadioPad|||")
          .Append(now).Append("||ORU^R01^ORU_R01|").Append(msgId)
          .Append("|P|2.5").Append(SegmentTerminator);

        // PID — Patient Identification. We carry the opaque patient reference
        // only; PHI minimisation keeps name/DOB out unless the customer maps
        // them at the integration layer.
        sb.Append("PID|1||").Append(patientRef).Append("^^^RadioPad^MR")
          .Append(SegmentTerminator);

        // OBR — Observation Request. Accession + study code.
        sb.Append("OBR|1|").Append(accession).Append('|').Append(accession).Append('|')
          .Append(modality).Append('^').Append(bodyPart).Append("^L|||")
          .Append(now).Append("|||||||||||||||")
          .Append(now).Append("||RAD||||||F").Append(SegmentTerminator);

        // OBX — one TX observation per report section that has content.
        var idx = 1;
        AppendSection(sb, ref idx, "INDICATION", report.Indication);
        AppendSection(sb, ref idx, "TECHNIQUE", report.Technique);
        AppendSection(sb, ref idx, "COMPARISON", report.Comparison);
        AppendSection(sb, ref idx, "FINDINGS", report.Findings);
        AppendSection(sb, ref idx, "IMPRESSION", report.Impression);
        AppendSection(sb, ref idx, "RECOMMENDATIONS", report.Recommendations);

        return sb.ToString();
    }

    private static void AppendSection(StringBuilder sb, ref int idx, string code, string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return;
        // OBX|<set-id>|TX|<code>^<code>^L||<value>||||||F
        // Multi-line content uses the HL7 repetition separator '~' between
        // logical lines, which is the canonical way to carry a multi-line TX.
        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => Escape(l.Trim()));
        var value = string.Join("~", lines);
        sb.Append("OBX|").Append(idx).Append("|TX|")
          .Append(code).Append('^').Append(code).Append("^L||")
          .Append(value).Append("||||||F").Append(SegmentTerminator);
        idx++;
    }

    /// <summary>
    /// HL7 v2 escape: replace the field/component/repetition/escape/subcomp
    /// delimiters with their escape sequences so the message remains parseable.
    /// </summary>
    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s
            .Replace("\\", "\\E\\")
            .Replace("|", "\\F\\")
            .Replace("^", "\\S\\")
            .Replace("&", "\\T\\")
            .Replace("~", "\\R\\");
    }
}
