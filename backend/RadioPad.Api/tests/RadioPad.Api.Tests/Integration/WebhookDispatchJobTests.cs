using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Api.Jobs;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// PR-N2 — <see cref="WebhookDispatchJob"/>. Drives <c>DeliverAuditEventAsync</c> directly with
/// the default HttpClient's primary handler swapped for a recorder. Confirms the payload is
/// PHI-minimized (no DetailsJson / clinical text), the <c>X-RadioPad-Signature</c> HMAC matches a
/// recomputation over the raw body, a 5xx throws (so the jittered-retry filter re-runs it) and
/// increments the failure count, and that the endpoint auto-disables (+ audits) at the threshold.
/// </summary>
public class WebhookDispatchJobTests
{
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public string? LastBody;
        public string? LastSignature;
        public string? LastUrl;
        public int CallCount;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            CallCount++;
            LastUrl = request.RequestUri?.ToString();
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            LastSignature = request.Headers.TryGetValues("X-RadioPad-Signature", out var v) ? v.FirstOrDefault() : null;
            return new HttpResponseMessage(Status);
        }
    }

    private sealed class WebhookTestFactory : RadioPadAppFactory
    {
        public readonly RecordingHandler Handler = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                // Override the default (unnamed) HttpClient — the one WebhookDispatchJob creates.
                services.AddHttpClient(string.Empty).ConfigurePrimaryHttpMessageHandler(() => Handler);
            });
        }
    }

    private const string Secret = "webhook-secret-xyz";

    private static async Task<(Guid endpointId, Guid eventId)> SeedAsync(WebhookTestFactory factory, string detailsJson)
    {
        Guid endpointId, eventId;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var ep = new TenantWebhookEndpoint
        {
            TenantId = factory.SeedTenant.Id,
            Url = "https://webhook.example/ingest",
            Secret = Secret,
            EventsCsv = "audit",
            Active = true,
        };
        db.TenantWebhookEndpoints.Add(ep);
        await db.SaveChangesAsync();
        endpointId = ep.Id;

        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
        var evt = new AuditEvent
        {
            TenantId = factory.SeedTenant.Id,
            Action = AuditAction.ReportEdited,
            DetailsJson = detailsJson,
        };
        await audit.AppendAsync(evt, CancellationToken.None);
        eventId = evt.Id;
        return (endpointId, eventId);
    }

    [Fact]
    public async Task Deliver_Success_SignsPayload_AndIsPhiMinimized()
    {
        var factory = new WebhookTestFactory();
        await factory.InitializeAsync();
        try
        {
            var (endpointId, eventId) = await SeedAsync(factory, "{\"title\":\"PHI_MUST_NOT_LEAK\"}");
            factory.Handler.Status = HttpStatusCode.OK;

            var job = factory.Services.GetRequiredService<WebhookDispatchJob>();
            await job.DeliverAuditEventAsync(endpointId, eventId, CancellationToken.None);

            Assert.Equal(1, factory.Handler.CallCount);
            var body = factory.Handler.LastBody!;
            Assert.Contains("\"id\"", body);
            Assert.Contains("\"action\"", body);
            Assert.Contains("\"tenantId\"", body);
            Assert.Contains("\"createdAt\"", body);
            Assert.Contains("\"integrityChain\"", body);
            Assert.DoesNotContain("PHI_MUST_NOT_LEAK", body);
            Assert.DoesNotContain("title", body);

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
            var expected = "sha256=" + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
            Assert.Equal(expected, factory.Handler.LastSignature);
        }
        finally { await factory.DisposeAsync(); }
    }

    [Fact]
    public async Task Deliver_5xx_Throws_AndIncrementsFailureCount()
    {
        var factory = new WebhookTestFactory();
        await factory.InitializeAsync();
        try
        {
            var (endpointId, eventId) = await SeedAsync(factory, "{}");
            factory.Handler.Status = HttpStatusCode.InternalServerError;

            var job = factory.Services.GetRequiredService<WebhookDispatchJob>();
            await Assert.ThrowsAsync<HttpRequestException>(
                () => job.DeliverAuditEventAsync(endpointId, eventId, CancellationToken.None));

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var ep = await db.TenantWebhookEndpoints.SingleAsync(e => e.Id == endpointId);
            Assert.Equal(1, ep.FailureCount);
            Assert.True(ep.Active);
            Assert.Null(ep.DisabledAt);
        }
        finally { await factory.DisposeAsync(); }
    }

    [Fact]
    public async Task Deliver_RepeatedFailures_AutoDisablesAtThreshold_AndAudits()
    {
        var factory = new WebhookTestFactory();
        await factory.InitializeAsync();
        try
        {
            var (endpointId, eventId) = await SeedAsync(factory, "{}");
            factory.Handler.Status = HttpStatusCode.ServiceUnavailable;

            var job = factory.Services.GetRequiredService<WebhookDispatchJob>();
            for (int i = 0; i < WebhookDispatchJob.DisableThreshold; i++)
            {
                try { await job.DeliverAuditEventAsync(endpointId, eventId, CancellationToken.None); }
                catch (HttpRequestException) { /* expected — jittered retry would re-run in prod */ }
            }

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var ep = await db.TenantWebhookEndpoints.SingleAsync(e => e.Id == endpointId);
            Assert.False(ep.Active);
            Assert.NotNull(ep.DisabledAt);
            Assert.True(ep.FailureCount >= WebhookDispatchJob.DisableThreshold);

            var disabled = await db.AuditEvents.AnyAsync(a =>
                a.TenantId == factory.SeedTenant.Id && a.Action == AuditAction.WebhookEndpointDisabled);
            Assert.True(disabled);
        }
        finally { await factory.DisposeAsync(); }
    }
}
