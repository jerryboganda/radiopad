using System.Text.Json.Nodes;
using RadioPad.Application.Services.Hl7Bridge;
using Xunit;

namespace RadioPad.Api.Tests.Iter33;

/// <summary>
/// Iter-33 INT-008 — round-trip an ORU^R01 through HL7→SR→HL7 and assert
/// that AccessionNumber (OBR-3) and OBX-5 narrative survive both directions.
/// Also asserts the outbound SR carries a freshly minted SOPInstanceUID with
/// the documented <c>2.25.&lt;guid-as-int&gt;</c> shape.
/// </summary>
public class Hl7DicomSrRoundTripTests
{
    private const string SyntheticOru =
        "MSH|^~\\&|RIS|FAC|RAD|RAD|20260503120000||ORU^R01|MID-1|P|2.5\r" +
        "PID|1||PT-SYN-1\r" +
        "OBR|1||ACC1|71250^CT CHEST^L|||20260503120000||||||||||||||||CT||||||||||\r" +
        "OBX|1|TX|FINDING||No acute findings|||||F\r" +
        "OBX|2|TX|IMPRESSION||Normal study|||||F\r";

    [Fact]
    public void HL7_To_SR_Captures_Accession_And_Obx_Texts()
    {
        var msg = Hl7Message.Parse(SyntheticOru);
        var sr = Hl7ToDicomSrConverter.Convert(msg);

        Assert.Equal("ACC1", ((JsonArray)sr["00080050"]!["Value"]!)[0]!.ToString());

        var sopInstance = ((JsonArray)sr["00080018"]!["Value"]!)[0]!.ToString();
        Assert.StartsWith("2.25.", sopInstance);
        Assert.True(sopInstance.Length > 5);

        var content = (JsonArray)sr["0040A730"]!["Value"]!;
        Assert.Equal(2, content.Count);
        Assert.Equal("No acute findings", ((JsonArray)content[0]!["0040A160"]!["Value"]!)[0]!.ToString());
        Assert.Equal("Normal study", ((JsonArray)content[1]!["0040A160"]!["Value"]!)[0]!.ToString());
    }

    [Fact]
    public void Roundtrip_HL7_To_SR_To_HL7_Preserves_Accession_And_OBX5()
    {
        var inbound = Hl7Message.Parse(SyntheticOru);
        var sr = Hl7ToDicomSrConverter.Convert(inbound);
        var outbound = DicomSrToHl7Converter.Convert(sr);

        Assert.Equal("ACC1", outbound.AccessionNumber);
        Assert.Equal(2, outbound.Observations.Count);
        Assert.Equal("No acute findings", outbound.Observations[0].Value);
        Assert.Equal("Normal study", outbound.Observations[1].Value);

        // Re-serialize and re-parse to confirm OBR-3 lands on the wire.
        var wire = outbound.Serialize();
        Assert.Contains("OBR|1||ACC1|", wire);
        Assert.Contains("|No acute findings|", wire);

        var reparsed = Hl7Message.Parse(wire);
        Assert.Equal("ACC1", reparsed.AccessionNumber);
        Assert.Equal(2, reparsed.Observations.Count);
        Assert.Equal("No acute findings", reparsed.Observations[0].Value);
    }

    [Fact]
    public void Sop_Instance_Uid_Is_Unique_Across_Calls()
    {
        var a = Hl7ToDicomSrConverter.NewSopInstanceUid();
        var b = Hl7ToDicomSrConverter.NewSopInstanceUid();
        Assert.NotEqual(a, b);
        Assert.StartsWith("2.25.", a);
        Assert.StartsWith("2.25.", b);
    }
}
