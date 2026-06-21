using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Domain.ValueObjects;
using RadioPad.Infrastructure.Providers;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// Iter-0b — AI provenance + safety plumbing:
/// 0.2 ValidationFinding span anchors, 0.3 rulebook id/version into every AI
/// audit + usage row, 0.4 centralized temperature ceiling, 0.5 structured-output
/// (response_format) wiring in the shared OpenAI body builder.
/// </summary>
public class Iter0bAiProvenanceTests
{
    // ---- 0.4 temperature ceiling -------------------------------------------

    [Fact]
    public void Temperature_Defaults_To_Conservative_0_2()
    {
        var req = new AiCompletionRequest(Provider(), "s", "u", "v1", false);
        Assert.Equal(0.2, req.Temperature, 3);
    }

    [Theory]
    [InlineData(0.9, 0.4)]   // above ceiling → clamped to 0.4
    [InlineData(0.4, 0.4)]   // at ceiling → unchanged
    [InlineData(0.1, 0.1)]   // below ceiling → unchanged
    [InlineData(-1.0, 0.0)]  // negative → clamped to 0
    public void Temperature_Is_Clamped_To_Ceiling(double requested, double expected)
    {
        var req = new AiCompletionRequest(Provider(), "s", "u", "v1", false) { Temperature = requested };
        Assert.Equal(expected, req.Temperature, 3);
    }

    // ---- 0.3 rulebook provenance on audit + usage --------------------------

    [Fact]
    public async Task Gateway_Records_Rulebook_Provenance_On_Audit_And_Usage()
    {
        var audit = new RecordingAudit();
        var usage = new RecordingUsage();
        var gw = new AiGateway(new[] { (IAiProviderAdapter)new OkAdapter() }, audit,
            NullLogger<AiGateway>.Instance, usage);

        var rbId = Guid.NewGuid();
        var req = new AiCompletionRequest(Provider(), "sys", "x", "v1", false)
        {
            RulebookId = rbId,
            RulebookVersion = "1.2.3",
        };

        await gw.RouteAsync(Tenant(), req, default);

        var evt = Assert.Single(audit.Events);
        Assert.Equal(AuditAction.AiResponse, evt.Action);
        using var doc = JsonDocument.Parse(evt.DetailsJson);
        Assert.Equal(rbId, doc.RootElement.GetProperty("rulebookId").GetGuid());
        Assert.Equal("1.2.3", doc.RootElement.GetProperty("rulebookVersion").GetString());
        Assert.Equal(0.2, doc.RootElement.GetProperty("temperature").GetDouble(), 3);

        var row = Assert.Single(usage.Records);
        Assert.Equal(rbId, row.RulebookId);
        Assert.Equal("1.2.3", row.RulebookVersion);
    }

    // ---- 0.5 structured output in the shared OpenAI body -------------------

    [Fact]
    public void BuildChatBody_Always_Emits_Temperature()
    {
        var json = JsonSerializer.Serialize(
            OpenAiChatHelpers.BuildChatBody("m", "s", "u", temperature: 0.2));
        Assert.Contains("\"temperature\":0.2", json);
        Assert.DoesNotContain("response_format", json);
    }

    [Fact]
    public void BuildChatBody_Emits_ResponseFormat_For_Valid_Schema()
    {
        var schema = """{"type":"object","properties":{"impression":{"type":"string"}}}""";
        var json = JsonSerializer.Serialize(
            OpenAiChatHelpers.BuildChatBody("m", "s", "u", temperature: 0.2, outputSchema: schema));
        Assert.Contains("response_format", json);
        Assert.Contains("json_schema", json);
        Assert.Contains("radiopad_structured_output", json);
    }

    [Fact]
    public void BuildChatBody_Ignores_Malformed_Schema()
    {
        var json = JsonSerializer.Serialize(
            OpenAiChatHelpers.BuildChatBody("m", "s", "u", outputSchema: "{not valid json"));
        Assert.DoesNotContain("response_format", json);
    }

    // ---- 0.2 ValidationFinding span anchors --------------------------------

    [Fact]
    public void ValidationFinding_Carries_Optional_Span_Anchors()
    {
        var f = new ValidationFinding("ai:unsupported_claim", "Warning", "msg",
            Section: "Impression", Snippet: "mass", StartIndex: 10, EndIndex: 14);
        Assert.Equal(10, f.StartIndex);
        Assert.Equal(14, f.EndIndex);

        // Back-compat: existing call sites omit spans and get nulls.
        var legacy = new ValidationFinding("r", "Info", "m");
        Assert.Null(legacy.StartIndex);
        Assert.Null(legacy.EndIndex);
    }

    // ---- fakes -------------------------------------------------------------

    private static Tenant Tenant() => new() { Id = Guid.NewGuid(), Slug = "t1", DisplayName = "T1" };

    private static ProviderConfig Provider() =>
        new() { Id = Guid.NewGuid(), Name = "P", Adapter = "ok", Compliance = ProviderComplianceClass.PhiApproved, Enabled = true };

    private sealed class OkAdapter : IAiProviderAdapter
    {
        public string Id => "ok";
        public Task<AiResult> CompleteAsync(AiCompletionRequest request, CancellationToken ct) =>
            Task.FromResult(new AiResult("ok", request.Provider.Name, request.Provider.Model, 1, 10, 5, request.PromptVersion));
    }

    private sealed class RecordingAudit : IAuditLog
    {
        public List<AuditEvent> Events { get; } = new();
        public Task AppendAsync(AuditEvent evt, CancellationToken ct) { Events.Add(evt); return Task.CompletedTask; }
        public Task<IReadOnlyList<AuditEvent>> QueryAsync(Guid tenantId, DateTimeOffset? from, DateTimeOffset? to, int take = 200, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AuditEvent>>(Events);
        public Task<AuditChainVerification> VerifyChainAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(new AuditChainVerification(Events.Count, true, null, null));
    }

    private sealed class RecordingUsage : IAiUsageStore
    {
        public List<AiRequest> Records { get; } = new();
        public Task RecordAsync(AiRequest request, CancellationToken ct) { Records.Add(request); return Task.CompletedTask; }
        public Task<UsageSummary> SummariseAsync(Guid tenantId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
