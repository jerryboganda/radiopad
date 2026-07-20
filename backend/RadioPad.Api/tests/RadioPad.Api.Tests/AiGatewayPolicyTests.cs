using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Domain.ValueObjects;
using Xunit;

namespace RadioPad.Api.Tests;

public class AiGatewayPolicyTests
{
    private static (AiGateway gateway, RecordingAudit audit) BuildGateway()
    {
        var audit = new RecordingAudit();
        var adapters = new[] { (IAiProviderAdapter)new TestAdapter() };
        var gw = new AiGateway(adapters, audit, NullLogger<AiGateway>.Instance);
        return (gw, audit);
    }

    private static Tenant Tenant() => new() { Id = Guid.NewGuid(), Slug = "t1", DisplayName = "T1", RequirePhiApprovedProvider = true };

    private static ProviderConfig Provider(ProviderComplianceClass cls, bool enabled = true) =>
        new() { Id = Guid.NewGuid(), Name = "P", Adapter = "test", Compliance = cls, Enabled = enabled };

    // PHI gate removed (operator decision 2026-07-20): PHI routes to any
    // compliance class, including Sandbox and Blocked.
    [Fact]
    public async Task Phi_Request_To_Sandbox_Provider_Succeeds()
    {
        var (gw, audit) = BuildGateway();
        var req = new AiCompletionRequest(Provider(ProviderComplianceClass.Sandbox), "sys", "patient John Doe", "v1", ContainsPhi: true);
        var r = await gw.RouteAsync(Tenant(), req, default);
        Assert.Equal("ok", r.Text);
        Assert.Single(audit.Events);
        Assert.Equal(AuditAction.AiResponse, audit.Events[0].Action);
    }

    [Fact]
    public async Task Phi_Request_To_PhiApproved_Provider_Succeeds()
    {
        var (gw, audit) = BuildGateway();
        var req = new AiCompletionRequest(Provider(ProviderComplianceClass.PhiApproved), "sys", "patient John Doe", "v1", ContainsPhi: true);
        var r = await gw.RouteAsync(Tenant(), req, default);
        Assert.Equal("ok", r.Text);
        Assert.Single(audit.Events);
        Assert.Equal(AuditAction.AiResponse, audit.Events[0].Action);
    }

    [Fact]
    public async Task Phi_Request_To_LocalOnly_Provider_Succeeds()
    {
        var (gw, _) = BuildGateway();
        var req = new AiCompletionRequest(Provider(ProviderComplianceClass.LocalOnly), "sys", "phi", "v1", ContainsPhi: true);
        var r = await gw.RouteAsync(Tenant(), req, default);
        Assert.Equal("ok", r.Text);
    }

    [Fact]
    public async Task Disabled_Provider_Throws()
    {
        var (gw, _) = BuildGateway();
        var req = new AiCompletionRequest(Provider(ProviderComplianceClass.PhiApproved, enabled: false), "sys", "x", "v1", false);
        await Assert.ThrowsAsync<ProviderPolicyException>(() => gw.RouteAsync(Tenant(), req, default));
    }

    [Fact]
    public async Task Blocked_Compliance_Class_No_Longer_Blocks()
    {
        var (gw, _) = BuildGateway();
        var req = new AiCompletionRequest(Provider(ProviderComplianceClass.Blocked), "sys", "x", "v1", false);
        var r = await gw.RouteAsync(Tenant(), req, default);
        Assert.Equal("ok", r.Text);
    }

    [Fact]
    public async Task Adapter_Failure_Records_PolicyViolation_And_Rethrows()
    {
        var audit = new RecordingAudit();
        var adapters = new[] { (IAiProviderAdapter)new FailingAdapter() };
        var gw = new AiGateway(adapters, audit, NullLogger<AiGateway>.Instance);
        var req = new AiCompletionRequest(
            new ProviderConfig { Name = "X", Adapter = "fail", Compliance = ProviderComplianceClass.PhiApproved, Enabled = true },
            "sys", "x", "v1", false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => gw.RouteAsync(Tenant(), req, default));
        Assert.Single(audit.Events);
        Assert.Equal(AuditAction.PolicyViolation, audit.Events[0].Action);
    }

    [Fact]
    public async Task Adapter_Policy_Exception_Records_ProviderBlocked_And_Rethrows()
    {
        var audit = new RecordingAudit();
        var adapters = new[] { (IAiProviderAdapter)new PolicyFailingAdapter() };
        var gw = new AiGateway(adapters, audit, NullLogger<AiGateway>.Instance);
        var req = new AiCompletionRequest(
            new ProviderConfig { Name = "X", Adapter = "policy-fail", Compliance = ProviderComplianceClass.PhiApproved, Enabled = true },
            "sys", "x", "v1", false);
        await Assert.ThrowsAsync<ProviderPolicyException>(() => gw.RouteAsync(Tenant(), req, default));
        Assert.Single(audit.Events);
        Assert.Equal(AuditAction.ProviderBlocked, audit.Events[0].Action);
    }

    private sealed class TestAdapter : IAiProviderAdapter
    {
        public string Id => "test";
        public Task<AiResult> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new AiResult("ok", request.Provider.Name, request.Provider.Model, 1, 10, 5, request.PromptVersion));
    }

    private sealed class FailingAdapter : IAiProviderAdapter
    {
        public string Id => "fail";
        public Task<AiResult> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom");
    }

    private sealed class PolicyFailingAdapter : IAiProviderAdapter
    {
        public string Id => "policy-fail";
        public Task<AiResult> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken) =>
            throw new ProviderPolicyException("adapter policy blocked");
    }

    private sealed class RecordingAudit : IAuditLog
    {
        public List<AuditEvent> Events { get; } = new();
        public Task AppendAsync(AuditEvent evt, CancellationToken cancellationToken) { Events.Add(evt); return Task.CompletedTask; }
        public Task<IReadOnlyList<AuditEvent>> QueryAsync(Guid tenantId, DateTimeOffset? from, DateTimeOffset? to, int take = 200, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuditEvent>>(Events);
        public Task<AuditChainVerification> VerifyChainAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuditChainVerification(Events.Count, true, null, null));
    }
}
