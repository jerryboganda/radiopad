using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Api.Tests.Infrastructure;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// Phase B (dictation transcription) — unit tests for
/// <see cref="TranscriptionService"/>. Covers the happy path (de-identified
/// audio allowed on UBAG) and the PHI gate
/// (<see cref="AiGateway.EnforceTranscriptionPolicy"/>): PHI audio is rejected
/// without the env opt-in + ack + compliant provider, and only allowed when all
/// three hold. UBAG + router + audit are faked; no network is touched.
/// </summary>
public class TranscriptionServiceTests
{
    private const string PhiAudioEnv = "RADIOPAD_ALLOW_PHI_AUDIO_TRANSCRIPTION";

    private static Tenant Tenant() =>
        new() { Id = Guid.NewGuid(), Slug = "t1", DisplayName = "T1", RequirePhiApprovedProvider = true };

    private static User User() =>
        new() { Id = Guid.NewGuid(), Email = "rad@radiopad.local", Role = UserRole.Radiologist };

    /// <summary>A report with no PHI-shaped content (de-identified).</summary>
    private static Report CleanReport() =>
        new() { Id = Guid.NewGuid(), Study = new StudyContext { Modality = "CT", BodyPart = "Chest" } };

    /// <summary>A report whose study carries a patient reference → PHI.</summary>
    private static Report PhiReport() =>
        new() { Id = Guid.NewGuid(), Study = new StudyContext { Modality = "CT", PatientReference = "patient-abc-123" } };

    private static ProviderConfig Provider(ProviderComplianceClass cls, string model = "gemini_web") =>
        new() { Id = Guid.NewGuid(), Name = "UBAG", Adapter = "ubag", Model = model, Compliance = cls, Enabled = true };

    private static TranscriptionService Build(ProviderConfig? selected, FakeUbag ubag, RecordingAudit audit) =>
        new(ubag, new FakeRouter(selected), audit, NullLogger<TranscriptionService>.Instance);

    private static MemoryStream Audio() => new(new byte[] { 1, 2, 3, 4 });

    // ── de-identified happy path ───────────────────────────────────────────────

    [Fact]
    public async Task Deidentified_Audio_Is_Transcribed_On_Ubag()
    {
        var ubag = new FakeUbag();
        var audit = new RecordingAudit();
        var svc = Build(Provider(ProviderComplianceClass.Sandbox), ubag, audit);

        var result = await svc.TranscribeAsync(
            Tenant(), User(), CleanReport(), Audio(), "dictation.webm", 4, "audio/webm",
            deidentifiedAck: false, CancellationToken.None);

        Assert.Equal("transcript text", result.Text);
        Assert.Equal("UBAG", result.Provider);
        Assert.Equal("gemini_web", result.Model);

        // Job created FIRST, audio uploaded SECOND, both with the same idempotency key.
        Assert.True(ubag.JobCreatedBeforeUpload);
        Assert.Equal("dictation.webm", ubag.UploadedKey);
        Assert.Equal("medical_transcription_target:gemini_web", ubag.CreatedSignature);
    }

    [Fact]
    public async Task Successful_Transcription_Audits_AudioTranscribed_With_Sha256_Only()
    {
        var ubag = new FakeUbag();
        var audit = new RecordingAudit();
        var svc = Build(Provider(ProviderComplianceClass.Sandbox), ubag, audit);

        await svc.TranscribeAsync(
            Tenant(), User(), CleanReport(), Audio(), "dictation.webm", 4, "audio/webm",
            false, CancellationToken.None);

        var evt = Assert.Single(audit.Events);
        Assert.Equal(AuditAction.AudioTranscribed, evt.Action);
        // The transcript text must never be persisted — only its SHA-256.
        Assert.DoesNotContain("transcript text", evt.DetailsJson);
        Assert.Contains("transcriptSha256", evt.DetailsJson);
    }

    // ── PHI gate ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Phi_Audio_Rejected_When_Env_Not_Set()
    {
        using var env = EnvVarScope.Set(PhiAudioEnv, null);
        var svc = Build(Provider(ProviderComplianceClass.PhiApproved), new FakeUbag(), new RecordingAudit());

        var ex = await Assert.ThrowsAsync<ProviderPolicyException>(() => svc.TranscribeAsync(
            Tenant(), User(), PhiReport(), Audio(), "dictation.webm", 4, "audio/webm",
            deidentifiedAck: true, CancellationToken.None));

        Assert.Contains("audio_requires_deidentified", ex.Message);
    }

    [Fact]
    public async Task Phi_Audio_Rejected_When_Ack_Missing()
    {
        using var env = EnvVarScope.Set(PhiAudioEnv, "1");
        var svc = Build(Provider(ProviderComplianceClass.PhiApproved), new FakeUbag(), new RecordingAudit());

        var ex = await Assert.ThrowsAsync<ProviderPolicyException>(() => svc.TranscribeAsync(
            Tenant(), User(), PhiReport(), Audio(), "dictation.webm", 4, "audio/webm",
            deidentifiedAck: false, CancellationToken.None));

        Assert.Contains("audio_requires_deidentified", ex.Message);
    }

    [Fact]
    public async Task Phi_Audio_Rejected_When_Provider_Not_Compliant()
    {
        using var env = EnvVarScope.Set(PhiAudioEnv, "1");
        var svc = Build(Provider(ProviderComplianceClass.Sandbox), new FakeUbag(), new RecordingAudit());

        var ex = await Assert.ThrowsAsync<ProviderPolicyException>(() => svc.TranscribeAsync(
            Tenant(), User(), PhiReport(), Audio(), "dictation.webm", 4, "audio/webm",
            deidentifiedAck: true, CancellationToken.None));

        Assert.Contains("audio_requires_deidentified", ex.Message);
    }

    [Theory]
    [InlineData(ProviderComplianceClass.PhiApproved)]
    [InlineData(ProviderComplianceClass.LocalOnly)]
    public async Task Phi_Audio_Allowed_When_Env_And_Ack_And_Compliant(ProviderComplianceClass cls)
    {
        using var env = EnvVarScope.Set(PhiAudioEnv, "1");
        var ubag = new FakeUbag();
        var svc = Build(Provider(cls), ubag, new RecordingAudit());

        var result = await svc.TranscribeAsync(
            Tenant(), User(), PhiReport(), Audio(), "dictation.webm", 4, "audio/webm",
            deidentifiedAck: true, CancellationToken.None);

        Assert.Equal("transcript text", result.Text);
    }

    [Fact]
    public async Task No_Matching_Provider_Throws_ProviderPolicyException()
    {
        var svc = Build(selected: null, new FakeUbag(), new RecordingAudit());

        await Assert.ThrowsAsync<ProviderPolicyException>(() => svc.TranscribeAsync(
            Tenant(), User(), CleanReport(), Audio(), "dictation.webm", 4, "audio/webm",
            false, CancellationToken.None));
    }

    [Fact]
    public async Task Oversize_Audio_Throws_ArgumentException()
    {
        var svc = Build(Provider(ProviderComplianceClass.Sandbox), new FakeUbag(), new RecordingAudit());

        await Assert.ThrowsAsync<ArgumentException>(() => svc.TranscribeAsync(
            Tenant(), User(), CleanReport(), Audio(), "dictation.webm",
            TranscriptionService.MaxAudioBytes + 1, "audio/webm", false, CancellationToken.None));
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeRouter : IProviderRouter
    {
        private readonly ProviderConfig? _selected;
        public FakeRouter(ProviderConfig? selected) => _selected = selected;
        public Task<ProviderConfig?> SelectAsync(Tenant tenant, bool containsPhi, CancellationToken ct) =>
            Task.FromResult(_selected);
    }

    private sealed class FakeUbag : IUbagClient
    {
        public string? CreatedSignature { get; private set; }
        public string? UploadedKey { get; private set; }
        public bool JobCreatedBeforeUpload { get; private set; }
        private bool _jobCreated;

        public Task<UbagJob> CreateTranscriptionJobAsync(UbagTranscriptionRequest request, string idempotencyKey, CancellationToken ct)
        {
            _jobCreated = true;
            CreatedSignature = $"medical_transcription_target:{request.Target}";
            return Task.FromResult(new UbagJob("job_tx_1", request.Target, "completed", true, "transcript text", null, null, 30, "{}"));
        }

        public Task<UbagArtifact> UploadJobArtifactAsync(string jobId, string key, Stream content, string contentType, long contentLength, string idempotencyKey, CancellationToken ct)
        {
            JobCreatedBeforeUpload = _jobCreated;
            UploadedKey = key;
            return Task.FromResult(new UbagArtifact(jobId, key, contentType, contentLength, "sha256:stub"));
        }

        public Task<UbagJob> GetJobAsync(string jobId, CancellationToken ct) =>
            Task.FromResult(new UbagJob(jobId, "gemini_web", "completed", true, "transcript text", null, null, 30, "{}"));

        // Unused members.
        public Task<UbagHealth> GetHealthAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<UbagBrowserSummary> GetBrowserSummaryAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<UbagTarget>> ListTargetsAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<UbagBrowserContext>> ListBrowserContextsAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<UbagJob> CreateJobAsync(UbagJobRequest request, string idempotencyKey, CancellationToken ct) => throw new NotImplementedException();
        public Task<UbagWorkflow> CreateWorkflowAsync(UbagWorkflowRequest request, string idempotencyKey, CancellationToken ct) => throw new NotImplementedException();
        public Task<UbagWorkflowRun> RunWorkflowAsync(string workflowId, string idempotencyKey, CancellationToken ct) => throw new NotImplementedException();
        public Task<UbagWorkflowRun> GetWorkflowRunAsync(string runId, CancellationToken ct) => throw new NotImplementedException();
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
