using System.Numerics;
using System.Text.Json.Nodes;

namespace RadioPad.Application.Services.Hl7Bridge;

/// <summary>
/// Iter-33 INT-008 — converts an inbound HL7 v2 ORU^R01 message into a DICOM
/// Structured Report JSON model (DCM4CHE-style tag dictionary). Mappings:
/// <list type="bullet">
///   <item>OBR-3 (filler order / accession) → <c>00080050</c> AccessionNumber</item>
///   <item>each OBX TEXT segment → <c>0040A730</c> ContentSequence item with ValueType=TEXT, TextValue=OBX-5</item>
///   <item>fresh SOP Instance UID at <c>00080018</c> (<c>2.25.&lt;guid-as-int&gt;</c>)</item>
///   <item>SOP Class UID = Basic Text SR (<c>1.2.840.10008.5.1.4.1.1.88.11</c>)</item>
///   <item>root ValueType <c>0040A040</c> = CONTAINER</item>
/// </list>
/// The output is a <see cref="JsonObject"/> so it round-trips trivially over
/// HTTP and can be consumed/produced by tests without any third-party DICOM
/// stack.
/// </summary>
public static class Hl7ToDicomSrConverter
{
    public const string SopClassUidBasicTextSr = "1.2.840.10008.5.1.4.1.1.88.11";

    public static JsonObject Convert(Hl7Message hl7)
    {
        ArgumentNullException.ThrowIfNull(hl7);

        var sopInstanceUid = NewSopInstanceUid();

        var contentSequence = new JsonArray();
        foreach (var obx in hl7.Observations)
        {
            // Only TX (text) observations are mapped to TEXT content items.
            // Other OBX value types (NM, CE, …) are skipped by the v0.1 bridge
            // and would be handled by future extensions.
            if (!string.Equals(obx.ValueType, "TX", StringComparison.OrdinalIgnoreCase))
                continue;
            contentSequence.Add(new JsonObject
            {
                ["00400A40"] = Tag("CS", obx.ValueType),
                ["0040A040"] = Tag("CS", "TEXT"),
                ["0040A043"] = ConceptNameCodeSequence(obx.ObservationId),
                ["0040A160"] = Tag("UT", obx.Value),
            });
        }

        return new JsonObject
        {
            ["00080016"] = Tag("UI", SopClassUidBasicTextSr),
            ["00080018"] = Tag("UI", sopInstanceUid),
            ["00080050"] = Tag("SH", hl7.AccessionNumber),
            ["00100020"] = Tag("LO", hl7.PatientReference),
            ["0040A040"] = Tag("CS", "CONTAINER"),
            ["0040A730"] = new JsonObject
            {
                ["vr"] = "SQ",
                ["Value"] = contentSequence,
            },
        };
    }

    /// <summary>Generate a DICOM-compliant <c>2.25.&lt;guid-as-positive-int&gt;</c> UID.</summary>
    public static string NewSopInstanceUid()
    {
        var bytes = Guid.NewGuid().ToByteArray();
        // Force positive sign by appending a zero byte (BigInteger ctor is little-endian).
        var withSign = new byte[bytes.Length + 1];
        Buffer.BlockCopy(bytes, 0, withSign, 0, bytes.Length);
        var bi = new BigInteger(withSign);
        return "2.25." + bi.ToString();
    }

    private static JsonObject Tag(string vr, string value)
        => new()
        {
            ["vr"] = vr,
            ["Value"] = new JsonArray { value ?? string.Empty },
        };

    private static JsonObject ConceptNameCodeSequence(string code)
        => new()
        {
            ["vr"] = "SQ",
            ["Value"] = new JsonArray
            {
                new JsonObject
                {
                    ["00080100"] = Tag("SH", string.IsNullOrEmpty(code) ? "FINDING" : code),
                    ["00080102"] = Tag("SH", "RADIOPAD"),
                    ["00080104"] = Tag("LO", string.IsNullOrEmpty(code) ? "Finding" : code),
                },
            },
        };
}
