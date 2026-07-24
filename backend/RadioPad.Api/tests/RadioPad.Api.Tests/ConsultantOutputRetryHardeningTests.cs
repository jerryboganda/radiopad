using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Dictation;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Domain.ValueObjects;
using RadioPad.Validation.Engine;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// Hardening for UBAG's browser-automation Gemini web session (UBAG is deliberately RadioPad's
/// ONLY report-writing provider — these tests prove the retry NEVER substitutes a different or
/// pinned provider; it only re-asks the exact same <see cref="ProviderConfig"/> it was given).
/// The model has no pinned version, so its adherence to the mandatory FINDINGS heading+bullet /
/// numbered IMPRESSION layout varies run to run. <see cref="ConsultantOutputCompliance"/>
/// deterministically checks the response and both <see cref="ReportingService.GenerateStructuredAsync"/>
/// and <see cref="ReportRewriteService.RewriteAsync"/> re-ask the same provider once with the exact
/// violations spelled out before shipping anything to the radiologist.
/// </summary>
public class ConsultantOutputRetryHardeningTests
{
    // ---- Generate path -------------------------------------------------------------------

    [Fact]
    public async Task Generate_Retries_The_Same_Provider_Once_When_The_First_Attempt_Is_Noncompliant()
    {
        const string nonCompliant = """
            {"indication":"i","technique":"t","findings":"The kidneys are normal.","impression":"Calc.","recommendations":"None."}
            """;
        const string compliant = """
            {"indication":"i","technique":"t","findings":"KIDNEYS:\n• Normal.","impression":"1. Normal study.","recommendations":"None."}
            """;
        var gw = new SequencedGateway(nonCompliant, compliant);
        var svc = new ReportingService(gw, new StubRulebookStore(null), new NoAudit(), new ReportValidator(),
            NullLogger<ReportingService>.Instance);
        var provider = Provider();

        var result = await svc.GenerateStructuredAsync(Tenant(), User(), Report(), provider, default);

        Assert.Equal(2, gw.Requests.Count);
        // Never a different or pinned provider — the exact same ProviderConfig, by id, both times.
        Assert.All(gw.Requests, r => Assert.Equal(provider.Id, r.Provider.Id));
        Assert.Contains("FORMAT CORRECTION REQUIRED", gw.Requests[1].UserPrompt);
        Assert.Contains("heading", gw.Requests[1].UserPrompt);
        Assert.Equal("KIDNEYS:\n• Normal.", result.Findings);
        Assert.Equal("1. Normal study.", result.Impression);
    }

    [Fact]
    public async Task Generate_Does_Not_Retry_When_The_First_Attempt_Is_Already_Compliant()
    {
        const string compliant = """
            {"indication":"i","technique":"t","findings":"KIDNEYS:\n• Normal.","impression":"1. Normal study.","recommendations":"None."}
            """;
        var gw = new SequencedGateway(compliant);
        var svc = new ReportingService(gw, new StubRulebookStore(null), new NoAudit(), new ReportValidator(),
            NullLogger<ReportingService>.Instance);

        await svc.GenerateStructuredAsync(Tenant(), User(), Report(), Provider(), default);

        Assert.Single(gw.Requests);
    }

    [Fact]
    public async Task Generate_Is_Bounded_And_Ships_The_Best_Attempt_When_Every_Attempt_Is_Noncompliant()
    {
        const string nonCompliant = """
            {"indication":"i","technique":"t","findings":"The kidneys are normal.","impression":"Calc.","recommendations":"None."}
            """;
        var gw = new SequencedGateway(nonCompliant, nonCompliant, nonCompliant, nonCompliant);
        var svc = new ReportingService(gw, new StubRulebookStore(null), new NoAudit(), new ReportValidator(),
            NullLogger<ReportingService>.Instance);

        var result = await svc.GenerateStructuredAsync(Tenant(), User(), Report(), Provider(), default);

        // Bounded at MaxAttempts — never spins forever chasing compliance against a browser-driven
        // provider, and it still returns a usable (if imperfect) draft rather than throwing.
        Assert.Equal(ConsultantOutputCompliance.MaxAttempts, gw.Requests.Count);
        Assert.Equal("The kidneys are normal.", result.Findings);
    }

    // ---- Rewrite path ---------------------------------------------------------------------

    [Fact]
    public async Task Rewrite_Retries_The_Same_Provider_Once_When_The_First_Attempt_Is_Noncompliant()
    {
        var nonCompliant = RewriteBody(findings: "The kidneys are normal.", impression: "Calc.");
        var compliant = RewriteBody(findings: "KIDNEYS:\n• Normal.", impression: "1. Normal study.");
        var gw = new SequencedGateway(nonCompliant, compliant);
        var svc = new ReportRewriteService(gw);
        var provider = Provider();

        var result = await svc.RewriteAsync(
            Tenant(), Report(), provider, ReportRewriteMode.Concise, sections: null, instruction: null, ct: default);

        Assert.Equal(2, gw.Requests.Count);
        Assert.All(gw.Requests, r => Assert.Equal(provider.Id, r.Provider.Id));
        Assert.Contains("FORMAT CORRECTION REQUIRED", gw.Requests[1].UserPrompt);
        Assert.Equal(compliant, result.Text);
    }

    [Fact]
    public async Task Rewrite_Does_Not_Check_Layout_For_PatientFriendly_Or_ReferringSummary()
    {
        // These modes' plain-language / one-paragraph output is never in the heading+bullet
        // layout by design, so the hardening must not waste a retry chasing an impossible bar.
        var proseOnly = "Some plain-language paragraph with no headings or bullets at all.";
        var gw = new SequencedGateway(proseOnly);
        var svc = new ReportRewriteService(gw);

        await svc.RewriteAsync(
            Tenant(), Report(), Provider(), ReportRewriteMode.PatientFriendly, sections: null, instruction: null, ct: default);

        Assert.Single(gw.Requests);
    }

    [Fact]
    public async Task Rewrite_Custom_Mode_Still_Applies_The_Fabrication_Guard_After_A_Layout_Retry()
    {
        var nonCompliant = RewriteBody(findings: "The kidneys are normal.", impression: "Calc.");
        // The retry response fabricates a brand-new measurement not in the source report — the
        // layout-retry hardening must not bypass the pre-existing §5.3 no-fabrication guard.
        var compliantButFabricated = RewriteBody(findings: "KIDNEYS:\n• A new 9.9 cm mass.", impression: "1. Mass.");
        var gw = new SequencedGateway(nonCompliant, compliantButFabricated);
        var svc = new ReportRewriteService(gw);

        var result = await svc.RewriteAsync(
            Tenant(), Report(), Provider(), ReportRewriteMode.Custom, sections: null,
            instruction: "Tighten the wording.", ct: default);

        Assert.Equal(2, gw.Requests.Count);
        Assert.NotEmpty(result.Violations);
        Assert.Contains(result.Violations, v => v.Reason == ValidationRejectReason.AddedMeasurement);
    }

    // ---- harness --------------------------------------------------------------------------

    private static string RewriteBody(string findings, string impression) => $$"""
        Modality: CT
        Body part: KUB
        Indication: Suspected urolithiasis.

        INDICATION:
        Suspected urolithiasis.

        FINDINGS:
        {{findings}}

        IMPRESSION:
        {{impression}}

        INSTRUCTION: Rewrite the report under the rules in the system prompt. Output the rewritten report as plain text with the same section headings. Do not sign the report.
        """;

    private static Tenant Tenant() => new() { Id = Guid.NewGuid(), Slug = "t1", DisplayName = "T1" };
    private static User User() => new() { Id = Guid.NewGuid(), Email = "r@x.io", DisplayName = "Dr R", Role = UserRole.Radiologist };
    private static ProviderConfig Provider() =>
        new() { Id = Guid.NewGuid(), Name = "P", Adapter = "mock", Compliance = ProviderComplianceClass.PhiApproved, Enabled = true };

    private static Report Report() => new()
    {
        Id = Guid.NewGuid(),
        Indication = "Suspected urolithiasis.",
        Findings = "Right lower pole renal calculus 5.5 x 7.1 mm, non-obstructive.",
        Impression = "1. Renal calculus.",
        Study = new StudyContext { Modality = "CT", BodyPart = "KUB", Contrast = "Without contrast", Age = 59, Gender = "Male" },
    };

    /// <summary>Returns each queued response in order, one per call; the last response repeats once
    /// the queue is exhausted (so a bounded-retry test can supply exactly MaxAttempts responses).
    /// Records every request so a test can assert the provider never changed and a retry carried
    /// the reinforcement text.</summary>
    private sealed class SequencedGateway : IAiGateway
    {
        private readonly Queue<string> _responses;
        private string _last;
        public List<AiCompletionRequest> Requests { get; } = new();

        public SequencedGateway(params string[] responses)
        {
            _responses = new Queue<string>(responses);
            _last = responses.Length > 0 ? responses[^1] : string.Empty;
        }

        public Task<AiResult> RouteAsync(Tenant tenant, AiCompletionRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            var text = _responses.Count > 0 ? _responses.Dequeue() : _last;
            return Task.FromResult(new AiResult(text, tenant.Slug, "m", 1, 10, 5, request.PromptVersion));
        }
    }

    private sealed class StubRulebookStore : IRulebookStore
    {
        public static readonly Guid FixedId = Guid.NewGuid();
        private readonly string? _yaml;
        public StubRulebookStore(string? yaml) => _yaml = yaml;

        public Task<IReadOnlyList<Rulebook>> ListAsync(Guid tenantId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Rulebook>>(Array.Empty<Rulebook>());
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
