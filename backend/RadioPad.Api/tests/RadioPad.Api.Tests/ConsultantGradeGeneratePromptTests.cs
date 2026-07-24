using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Domain.ValueObjects;
using RadioPad.Validation.Engine;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// Guards the consultant-grade whole-report generation prompt (PromptVersion
/// v2.2026.07). The previous prompt told the model to write "a concise clinical
/// outline … no redundant prose" and fell back to the rulebook's terse
/// dictation-cleanup <c>draft</c> block, which produced junior-resident-grade
/// reports. These tests capture the composed request via a fake gateway and assert
/// the doctrine that the live UBAG gemini_web validation confirmed end-to-end.
/// </summary>
public class ConsultantGradeGeneratePromptTests
{
    [Fact]
    public async Task Generate_Prompt_Carries_Consultant_Doctrine_And_Drops_Terse_Wording()
    {
        var (gw, svc) = Build();
        await svc.GenerateStructuredAsync(Tenant(), User(), Report(), Provider(), default);

        // Normalise whitespace: the prompt is authored as a wrapped raw-string
        // literal, so sentences carry source line-breaks that are insignificant to
        // the model. Assert on meaning, not on source wrapping.
        var sys = Squish(gw.Captured!.SystemPrompt);
        var user = Squish(gw.Captured!.UserPrompt);

        // Consultant persona + systematic-review doctrine in the system prompt.
        Assert.Contains("senior consultant radiologist", sys);
        Assert.Contains("systematic review", sys);
        Assert.Contains("pertinent negatives", sys);
        Assert.Contains("within the limits of a non-contrast examination", sys);

        // REPORT REQUIREMENTS block replaced the terse FINDINGS FORMAT block.
        Assert.Contains("REPORT REQUIREMENTS", user);
        Assert.Contains("Systematic coverage", user);
        Assert.Contains("pertinent negatives", user);
        Assert.DoesNotContain("concise clinical outline", user);
        Assert.DoesNotContain("redundant prose", user);

        // Output contract forbids the fence/label that the gateway scraper flattens.
        Assert.Contains("do not wrap it in a Markdown code fence", user);
        Assert.Contains("language label", user);

        // Optional: emit the exact composed prompt (as ComposePrompt would build it)
        // so a byte-exact live re-validation can be run against the real gemini_web lane.
        var dump = Environment.GetEnvironmentVariable("RADIOPAD_DUMP_PROMPT");
        if (!string.IsNullOrEmpty(dump))
            System.IO.File.WriteAllText(dump,
                $"SYSTEM: {gw.Captured!.SystemPrompt?.Trim()}\n\nUSER: {gw.Captured!.UserPrompt?.Trim()}");
    }

    private static string Squish(string? s) =>
        System.Text.RegularExpressions.Regex.Replace(s ?? "", "\\s+", " ").Trim();

    [Fact]
    public async Task Generate_Does_Not_Fall_Back_To_The_Terse_Draft_Block()
    {
        // A rulebook whose ONLY generation-relevant block is the old terse `draft`
        // instruction must NOT drive whole-report generation — the consultant
        // default is used instead. (The `draft` block still serves the free-text
        // draft mode; it just no longer starves the guided-intake generator.)
        var terseDraft = "Generate a structured draft. Convert dictation into clean clinical prose "
            + "without adding findings. Keep it brief.";
        var (gw, svc) = Build(rulebookYaml: RulebookWithDraft(terseDraft));

        await svc.GenerateStructuredAsync(Tenant(), User(), Report(withRulebook: true), Provider(), default);

        var user = gw.Captured!.UserPrompt ?? "";
        Assert.DoesNotContain("without adding findings", user);
        Assert.DoesNotContain("Keep it brief", user);
        Assert.Contains("consultant-grade structured radiology report", user);
    }

    [Fact]
    public async Task Rulebook_Generate_Block_Overrides_The_Default_Instruction()
    {
        // A rulebook that DOES author a `generate` block wins over the code default.
        var customGenerate = "SENTINEL custom generate instruction for this exam.";
        var (gw, svc) = Build(rulebookYaml: RulebookWithGenerate(customGenerate));

        await svc.GenerateStructuredAsync(Tenant(), User(), Report(withRulebook: true), Provider(), default);

        var user = gw.Captured!.UserPrompt ?? "";
        Assert.Contains("SENTINEL custom generate instruction", user);
        // The requirements block is still appended regardless of the instruction source.
        Assert.Contains("REPORT REQUIREMENTS", user);
    }

    [Fact]
    public async Task Rulebook_System_Block_Refines_But_Never_Clobbers_The_Consultant_Doctrine()
    {
        // Option 1 (consultant-everywhere): a rulebook that authors its own (weaker)
        // `system` block must NOT replace the consultant doctrine — the doctrine leads
        // and the rulebook text is appended as modality refinement. ALL 114 shipped
        // rulebooks author a `system` block, so this is the production path, and its
        // clobber was the dominant cause of "basic" reports.
        var terseSystem = "You are assisting with a CT KUB report. Keep it brief.";
        var (gw, svc) = Build(rulebookYaml: RulebookWithSystem(terseSystem));

        await svc.GenerateStructuredAsync(Tenant(), User(), Report(withRulebook: true), Provider(), default);

        var sys = Squish(gw.Captured!.SystemPrompt);

        // The consultant doctrine survives in full...
        Assert.Contains("senior consultant radiologist", sys);
        Assert.Contains("systematic review", sys);
        Assert.Contains("pertinent negatives", sys);
        // ...and the rulebook's own guidance is appended as refinement, not dropped.
        Assert.Contains("Keep it brief", sys);
        Assert.Contains("Modality-specific guidance", sys);
    }

    // ---- harness -----------------------------------------------------------

    private static (CapturingGateway, ReportingService) Build(string? rulebookYaml = null)
    {
        var gw = new CapturingGateway();
        var store = new StubRulebookStore(rulebookYaml);
        var svc = new ReportingService(gw, store, new NoAudit(), new ReportValidator(),
            NullLogger<ReportingService>.Instance);
        return (gw, svc);
    }

    private static Tenant Tenant() => new() { Id = Guid.NewGuid(), Slug = "t1", DisplayName = "T1" };
    private static User User() => new() { Id = Guid.NewGuid(), Email = "r@x.io", DisplayName = "Dr R", Role = UserRole.Radiologist };
    private static ProviderConfig Provider() =>
        new() { Id = Guid.NewGuid(), Name = "P", Adapter = "mock", Compliance = ProviderComplianceClass.PhiApproved, Enabled = true };

    private static Report Report(bool withRulebook = false) => new()
    {
        Id = Guid.NewGuid(),
        RulebookId = withRulebook ? StubRulebookStore.FixedId : null,
        Indication = "Suspected urolithiasis.",
        Findings = "Right lower pole renal calculus 5.5 x 7.1 mm, 686 HU, non-obstructive.",
        Study = new StudyContext { Modality = "CT", BodyPart = "KUB", Contrast = "Without contrast", Age = 59, Gender = "Male" },
    };

    private static string RulebookWithDraft(string draft) => $"""
        rulebook_id: ct_kub_test
        name: CT KUB Test
        version: 1.1.0
        owner: it
        status: approved
        applies_to:
          modalities: [CT]
          body_parts: [KUB]
        required_sections: []
        rules: []
        prompt_blocks:
          system: "You are a senior consultant radiologist."
          draft: "{draft}"
        """;

    private static string RulebookWithGenerate(string generate) => $"""
        rulebook_id: ct_kub_test
        name: CT KUB Test
        version: 1.1.0
        owner: it
        status: approved
        applies_to:
          modalities: [CT]
          body_parts: [KUB]
        required_sections: []
        rules: []
        prompt_blocks:
          system: "You are a senior consultant radiologist."
          generate: "{generate}"
        """;

    private static string RulebookWithSystem(string system) => $"""
        rulebook_id: ct_kub_test
        name: CT KUB Test
        version: 1.1.0
        owner: it
        status: approved
        applies_to:
          modalities: [CT]
          body_parts: [KUB]
        required_sections: []
        rules: []
        prompt_blocks:
          system: "{system}"
        """;

    private sealed class CapturingGateway : IAiGateway
    {
        public AiCompletionRequest? Captured { get; private set; }
        public Task<AiResult> RouteAsync(Tenant tenant, AiCompletionRequest request, CancellationToken ct)
        {
            Captured ??= request;
            const string json = """
                {"indication":"i","technique":"t","findings":"KIDNEYS:\n• x","impression":"1. y","recommendations":"No specific follow-up is indicated."}
                """;
            return Task.FromResult(new AiResult(json, tenant.Slug, "m", 1, 10, 5, request.PromptVersion));
        }
    }

    private sealed class StubRulebookStore : IRulebookStore
    {
        public static readonly Guid FixedId = Guid.NewGuid();
        private readonly string? _yaml;
        public StubRulebookStore(string? yaml) => _yaml = yaml;

        public Task<IReadOnlyList<Rulebook>> ListAsync(Guid tenantId, CancellationToken ct)
        {
            IReadOnlyList<Rulebook> list = _yaml is null
                ? Array.Empty<Rulebook>()
                : new[]
                {
                    new Rulebook
                    {
                        Id = FixedId, TenantId = tenantId, RulebookId = "ct_kub_test", Version = "1.1.0",
                        Status = RulebookStatus.Approved, SourceYaml = _yaml,
                    },
                };
            return Task.FromResult(list);
        }

        public Task<Rulebook?> GetAsync(Guid tenantId, string rulebookId, CancellationToken ct) =>
            Task.FromResult<Rulebook?>(null);
        public Task SaveAsync(Rulebook rulebook, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class NoAudit : IAuditLog
    {
        public Task AppendAsync(AuditEvent evt, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<AuditEvent>> QueryAsync(Guid tenantId, DateTimeOffset? from, DateTimeOffset? to, int take = 200, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AuditEvent>>(Array.Empty<AuditEvent>());
        public Task<AuditChainVerification> VerifyChainAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(new AuditChainVerification(0, true, null, null));
    }
}
