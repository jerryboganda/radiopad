using System.Text.Json.Nodes;

namespace RadioPad.Application.Services.Hl7Bridge;

/// <summary>
/// Iter-33 INT-008 — reverse of <see cref="Hl7ToDicomSrConverter"/>. Walks the
/// DICOM SR <c>0040A730</c> ContentSequence collecting TEXT items into OBX-5
/// fields of an ORU^R01 message and propagates <c>00080050</c>
/// AccessionNumber to OBR-3 / OBR-4. The implementation is liberal in what
/// it accepts: any item with ValueType=TEXT and a 0040A160 string is mapped.
/// </summary>
public static class DicomSrToHl7Converter
{
    public static Hl7Message Convert(JsonObject sr,
        string sendingApplication = "RADIOPAD",
        string sendingFacility = "RADIOPAD",
        string receivingApplication = "RIS",
        string receivingFacility = "FAC")
    {
        ArgumentNullException.ThrowIfNull(sr);

        var accession = ReadStringValue(sr, "00080050") ?? string.Empty;
        var patientRef = ReadStringValue(sr, "00100020") ?? string.Empty;

        var obx = new List<Hl7ObxField>();
        if (sr["0040A730"] is JsonObject contentSeq
            && contentSeq["Value"] is JsonArray items)
        {
            int setId = 1;
            foreach (var node in items)
            {
                if (node is not JsonObject item) continue;
                var valueType = ReadStringValue(item, "0040A040") ?? string.Empty;
                if (!string.Equals(valueType, "TEXT", StringComparison.OrdinalIgnoreCase))
                    continue;
                var text = ReadStringValue(item, "0040A160") ?? string.Empty;
                var concept = ReadConceptCode(item) ?? "FINDING";
                obx.Add(new Hl7ObxField(setId, "TX", concept, text));
                setId++;
            }
        }

        return new Hl7Message(
            MessageType: "ORU^R01",
            SendingApplication: sendingApplication,
            SendingFacility: sendingFacility,
            ReceivingApplication: receivingApplication,
            ReceivingFacility: receivingFacility,
            MessageControlId: Guid.NewGuid().ToString("N").Substring(0, 18),
            ProcessingId: "P",
            VersionId: "2.5",
            AccessionNumber: accession,
            PatientReference: patientRef,
            Observations: obx);
    }

    private static string? ReadStringValue(JsonObject parent, string tag)
    {
        if (parent[tag] is not JsonObject t) return null;
        if (t["Value"] is not JsonArray arr || arr.Count == 0) return null;
        return arr[0]?.ToString();
    }

    private static string? ReadConceptCode(JsonObject contentItem)
    {
        if (contentItem["0040A043"] is not JsonObject seq) return null;
        if (seq["Value"] is not JsonArray arr || arr.Count == 0) return null;
        if (arr[0] is not JsonObject coded) return null;
        return ReadStringValue(coded, "00080100");
    }
}
