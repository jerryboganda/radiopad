using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// Phase B (dictation transcription) — unit tests for
/// <see cref="TranscriptionService"/>. Provider selection (and therefore PHI
/// routing) is delegated to <see cref="IProviderRouter"/>, exactly like the text
/// dictation/cleanup path — there is no separate audio gate. A de-identified
/// report transcribes on UBAG; when the router returns no compliant provider the
/// call is rejected. UBAG + router + audit are faked; no network is touched.
/// </summary>
public class TranscriptionServiceTests
{
    private static Tenant Tenant() =>
        new() { Id = Guid.NewGuid(), Slug = "t1", DisplayName = "T1", RequirePhiApprovedProvider = true };

    private static User User() =>
        new() { Id = Guid.NewGuid(), Email = "rad@radiopad.local", Role = UserRole.Radiologist };

    /// <summary>A report with no PHI-shaped content (de-identified).</summary>
    private static Report CleanReport() =>
        new() { Id = Guid.NewGuid(), Study = new StudyContext { Modality = "CT", BodyPart = "Chest" } };

    private static ProviderConfig Provider(ProviderComplianceClass cls, string model = "gemini_web") =>
        new() { Id = Guid.NewGuid(), Name = "UBAG", Adapter = "ubag", Model = model, Compliance = cls, Enabled = true };

    private static TranscriptionService Build(ProviderConfig? selected, FakeUbag ubag, RecordingAudit audit) =>
        new(ubag, new FakeRouter(selected), audit, NullLogger<TranscriptionService>.Instance);

    private static TranscriptionService Build(ProviderConfig? selected, FakeUbag ubag, RecordingAudit audit, ILocalSttClient localStt) =>
        new(ubag, new FakeRouter(selected), audit, NullLogger<TranscriptionService>.Instance, localStt);

    private static MemoryStream Audio() => new(new byte[] { 1, 2, 3, 4 });

    [Fact]
    public async Task Dictation_Audio_Is_Transcribed_On_Ubag()
    {
        var ubag = new FakeUbag();
        var audit = new RecordingAudit();
        var svc = Build(Provider(ProviderComplianceClass.Sandbox), ubag, audit);

        var result = await svc.TranscribeAsync(
            Tenant(), User(), CleanReport(), Audio(), "dictation.webm", 4, "audio/webm",
            CancellationToken.None);

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
            CancellationToken.None);

        var evt = Assert.Single(audit.Events);
        Assert.Equal(AuditAction.AudioTranscribed, evt.Action);
        // The transcript text must never be persisted — only its SHA-256.
        Assert.DoesNotContain("transcript text", evt.DetailsJson);
        Assert.Contains("transcriptSha256", evt.DetailsJson);
    }

    [Fact]
    public async Task No_Matching_Provider_Throws_ProviderPolicyException()
    {
        // The router refusing to select a compliant provider (returns null) is the
        // single PHI/compliance gate — identical to the text dictation path.
        var svc = Build(selected: null, new FakeUbag(), new RecordingAudit());

        await Assert.ThrowsAsync<ProviderPolicyException>(() => svc.TranscribeAsync(
            Tenant(), User(), CleanReport(), Audio(), "dictation.webm", 4, "audio/webm",
            CancellationToken.None));
    }

    [Fact]
    public async Task Oversize_Audio_Throws_ArgumentException()
    {
        var svc = Build(Provider(ProviderComplianceClass.Sandbox), new FakeUbag(), new RecordingAudit());

        await Assert.ThrowsAsync<ArgumentException>(() => svc.TranscribeAsync(
            Tenant(), User(), CleanReport(), Audio(), "dictation.webm",
            TranscriptionService.MaxAudioBytes + 1, "audio/webm", CancellationToken.None));
    }

    [Fact]
    public async Task Local_Engine_When_Available_Transcribes_Offline_And_Skips_Ubag()
    {
        // Desktop: an on-device engine is ready, so transcription runs fully
        // offline and the cloud (UBAG) flow is never reached — even though a
        // cloud provider is selectable.
        var ubag = new FakeUbag();
        var audit = new RecordingAudit();
        var local = new FakeLocalStt(new TranscriptionResult("on-device transcript", "local", "parakeet-tdt-0.6b-v3", 12));
        var svc = Build(Provider(ProviderComplianceClass.Sandbox), ubag, audit, local);

        var result = await svc.TranscribeAsync(
            Tenant(), User(), CleanReport(), Audio(), "dictation.webm", 4, "audio/webm",
            CancellationToken.None);

        Assert.Equal("on-device transcript", result.Text);
        Assert.Equal("local", result.Provider);
        Assert.Equal("parakeet-tdt-0.6b-v3", result.Model);
        Assert.Equal(1, local.Calls);

        // The cloud path must be completely untouched.
        Assert.Null(ubag.UploadedKey);
        Assert.Null(ubag.CreatedSignature);

        // Still audited — provenance + SHA-256 only, never the transcript text.
        var evt = Assert.Single(audit.Events);
        Assert.Equal(AuditAction.AudioTranscribed, evt.Action);
        Assert.DoesNotContain("on-device transcript", evt.DetailsJson);
        Assert.Contains("transcriptSha256", evt.DetailsJson);
        Assert.Contains("\"provider\":\"local\"", evt.DetailsJson);
    }

    [Fact]
    public async Task Local_Engine_Unavailable_Falls_Through_To_Ubag()
    {
        // Web/server (or a desktop without the model): the engine reports
        // unavailable, so the existing UBAG cloud flow handles transcription.
        var ubag = new FakeUbag();
        var audit = new RecordingAudit();
        var local = new FakeLocalStt(result: null); // Available == false
        var svc = Build(Provider(ProviderComplianceClass.Sandbox), ubag, audit, local);

        var result = await svc.TranscribeAsync(
            Tenant(), User(), CleanReport(), Audio(), "dictation.webm", 4, "audio/webm",
            CancellationToken.None);

        Assert.Equal("transcript text", result.Text);
        Assert.Equal("UBAG", result.Provider);
        Assert.Equal("dictation.webm", ubag.UploadedKey);
        Assert.Equal(0, local.Calls);
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeLocalStt : ILocalSttClient
    {
        private readonly TranscriptionResult? _result;
        public int Calls { get; private set; }
        public FakeLocalStt(TranscriptionResult? result) => _result = result;
        public bool Available => _result is not null;
        public Task<TranscriptionResult> TranscribeAsync(Stream audio, string contentType, CancellationToken ct, string? mode = null)
        {
            Calls++;
            return Task.FromResult(_result!);
        }
    }

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
