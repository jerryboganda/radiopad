using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using RadioPad.Domain.Entities;
using RadioPad.Validation.Engine;
using RadioPad.Validation.Rulebook;

var root = args[0];
var rulebooks = new[]
{
    "thyroid_us_v1","prostate_mri_v1","lung_screening_ct_v1","head_ct_trauma_v1",
    "knee_mri_v1","shoulder_mri_v1","abdomen_ct_v1","pelvis_mri_v1"
};
int failed = 0;
foreach (var rb in rulebooks)
{
    var yaml = File.ReadAllText(Path.Combine(root,"rulebooks",rb+".yaml"));
    var spec = RulebookSpec.FromYaml(yaml);
    Console.WriteLine($"== {rb} ({spec.RulebookId} v{spec.Version}) ==");
    var caseDir = Path.Combine(root,"rulebooks","_tests",rb);
    foreach (var f in Directory.GetFiles(caseDir,"*.json"))
    {
        var doc = JsonDocument.Parse(File.ReadAllText(f));
        var name = doc.RootElement.GetProperty("name").GetString();
        var rep = doc.RootElement.GetProperty("report");
        var r = new Report();
        if (rep.TryGetProperty("study", out var st))
        {
            r.Study.Modality = st.TryGetProperty("modality", out var m)?m.GetString()??"":"";
            r.Study.BodyPart = st.TryGetProperty("bodyPart", out var b)?b.GetString()??"":"";
            r.Study.Indication = st.TryGetProperty("indication", out var i)?i.GetString()??"":"";
            r.Study.AccessionNumber = st.TryGetProperty("accessionNumber", out var a)?a.GetString()??"":"";
        }
        r.Indication = rep.TryGetProperty("indication", out var ind)?ind.GetString()??"":"";
        r.Technique = rep.TryGetProperty("technique", out var tq)?tq.GetString()??"":"";
        r.Comparison = rep.TryGetProperty("comparison", out var cm)?cm.GetString()??"":"";
        r.Findings = rep.TryGetProperty("findings", out var fn)?fn.GetString()??"":"";
        r.Impression = rep.TryGetProperty("impression", out var ip)?ip.GetString()??"":"";
        r.Recommendations = rep.TryGetProperty("recommendations", out var rc)?rc.GetString()??"":"";
        var expected = doc.RootElement.GetProperty("expectFlagged").EnumerateArray().Select(x=>x.GetString()??"").ToArray();
        var v = new ReportValidator().Validate(r, spec);
        var actual = v.Findings.Select(x=>x.RuleId).Distinct().ToArray();
        var missing = expected.Except(actual).ToArray();
        var ok = missing.Length==0;
        if (!ok) failed++;
        Console.WriteLine($"  {(ok?"PASS":"FAIL")} {Path.GetFileName(f)} :: expected=[{string.Join(",",expected)}] actual=[{string.Join(",",actual)}]");
    }
}
Console.WriteLine($"\n{(failed==0?"ALL PASS":$"{failed} FAILURES")}");
return failed;
