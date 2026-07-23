using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// NOTIF-001 (PR-N4) — proves the workflow call sites actually produce notifications
/// after their state change + audit: critical-result create/escalate, the overdue
/// escalation sweep (deduped), peer-review assign/submit (with the blinding leak guard),
/// rulebook approve, and template submit/approve. Every assertion filters by the row's
/// DedupeKey so the shared class-fixture database cannot cross-contaminate counts.
/// </summary>
public sealed class NotificationProducerWiringTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;

    public NotificationProducerWiringTests(RadioPadAppFactory factory) => _factory = factory;

    // ── helpers ────────────────────────────────────────────────────────────

    private HttpClient ClientFor(string email)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-RadioPad-Tenant", _factory.SeedTenant.Slug);
        client.DefaultRequestHeaders.Add("X-RadioPad-User", email);
        return client;
    }

    private IServiceScope NewScope() => _factory.Services.CreateScope();
    private static RadioPadDbContext Db(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();

    private async Task<User> SeedUserAsync(UserRole role, string tag)
    {
        using var scope = NewScope();
        var user = new User
        {
            TenantId = _factory.SeedTenant.Id,
            Email = $"{tag}-{Guid.NewGuid():N}@radiopad.local",
            DisplayName = $"Seeded {tag}",
            Role = role,
            IsActive = true,
        };
        Db(scope).Users.Add(user);
        await Db(scope).SaveChangesAsync();
        return user;
    }

    private async Task<Guid> SeedReportAsync(Guid authorId)
    {
        using var scope = NewScope();
        var report = new Report { TenantId = _factory.SeedTenant.Id, CreatedByUserId = authorId };
        Db(scope).Reports.Add(report);
        await Db(scope).SaveChangesAsync();
        return report.Id;
    }

    private async Task<List<Notification>> NotificationsForAsync(Guid userId)
    {
        using var scope = NewScope();
        return await Db(scope).Notifications.AsNoTracking()
            .Where(n => n.TenantId == _factory.SeedTenant.Id && n.UserId == userId)
            .ToListAsync();
    }

    private static async Task<Guid> IdOfAsync(HttpResponseMessage resp)
    {
        var body = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, $"{(int)resp.StatusCode}: {body}");
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    // ── critical results ─────────────────────────────────────────────────────

    [Fact]
    public async Task CriticalResultCreate_NotifiesAuthor_CriticalRequiresAck()
    {
        var author = await SeedUserAsync(UserRole.Radiologist, "cr-author");
        var reportId = await SeedReportAsync(author.Id);

        // SeedUser (Radiologist) holds CriticalResultsManage and is NOT the author.
        var crId = await IdOfAsync(await _factory.CreateTenantClient().PostAsJsonAsync(
            "/api/critical-results",
            new { reportId, criticality = "Red", findingSummary = "Large right pneumothorax" }));

        var notif = Assert.Single(
            await NotificationsForAsync(author.Id), n => n.DedupeKey == $"crit-create:{crId}");
        Assert.Equal(NotificationCategory.CriticalResult, notif.Category);
        Assert.Equal(NotificationUrgency.Critical, notif.Urgency);
        Assert.True(notif.RequiresAck);
    }

    [Fact]
    public async Task CriticalResultEscalationSweep_NotifiesAuthorAndManagers_Deduped()
    {
        // Author is a ComplianceReviewer — read-only, NOT a CriticalResultsManage holder, so the
        // author's only notification is the direct author one (never a manager fan-out row).
        var author = await SeedUserAsync(UserRole.ComplianceReviewer, "esc-author");
        var reportId = await SeedReportAsync(author.Id);

        Guid crId;
        using (var scope = NewScope())
        {
            var cr = new CriticalResult
            {
                TenantId = _factory.SeedTenant.Id,
                ReportId = reportId,
                Criticality = Criticality.Red,
                FindingSummary = "Overdue finding",
                Status = CriticalResultStatus.Open,
                DueAt = DateTimeOffset.UtcNow.AddHours(-1),
                Notes = "",
            };
            Db(scope).CriticalResults.Add(cr);
            await Db(scope).SaveChangesAsync();
            crId = cr.Id;
        }

        var sweeper = new RadioPad.Api.Jobs.CriticalResultEscalationJob(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RadioPad.Api.Jobs.CriticalResultEscalationJob>.Instance);
        await sweeper.ScanOnceAsync(CancellationToken.None);

        // Force the same row back to overdue-Open and re-run: the DedupeKey `crit-esc:{id}` must
        // suppress the duplicate notification, so a re-run leaves exactly one per recipient.
        using (var scope = NewScope())
        {
            var cr = await Db(scope).CriticalResults.FirstAsync(c => c.Id == crId);
            cr.Status = CriticalResultStatus.Open;
            cr.DueAt = DateTimeOffset.UtcNow.AddHours(-1);
            cr.EscalatedAt = null;
            await Db(scope).SaveChangesAsync();
        }
        await sweeper.ScanOnceAsync(CancellationToken.None);

        var authorNotif = Assert.Single(
            await NotificationsForAsync(author.Id), n => n.DedupeKey == $"crit-esc:{crId}");
        Assert.Equal(NotificationUrgency.Critical, authorNotif.Urgency);
        Assert.True(authorNotif.RequiresAck);

        // SeedUser is a Radiologist → a CriticalResultsManage holder → exactly one manager row.
        var managerNotif = Assert.Single(
            await NotificationsForAsync(_factory.SeedUser.Id), n => n.DedupeKey == $"crit-esc:{crId}");
        Assert.Equal(NotificationCategory.CriticalResult, managerNotif.Category);
    }

    // ── peer review ──────────────────────────────────────────────────────────

    [Fact]
    public async Task PeerReviewSubmit_Blinded_NoReviewerIdentityInTitleOrBody()
    {
        var author = await SeedUserAsync(UserRole.Radiologist, "pr-author");
        var reportId = await SeedReportAsync(author.Id);

        // The reviewer is SeedUser (Radiologist → PeerReviewSubmit). Its identity must NEVER leak
        // into the author-facing notification while the review is blinded.
        Guid reviewId;
        using (var scope = NewScope())
        {
            var review = new PeerReview
            {
                TenantId = _factory.SeedTenant.Id,
                ReportId = reportId,
                ReviewerUserId = _factory.SeedUser.Id,
                OriginalAuthorUserId = author.Id,
                AssignedByUserId = author.Id,
                ReviewType = PeerReviewType.Targeted,
                Status = PeerReviewStatus.Assigned,
                Blinded = true,
            };
            Db(scope).PeerReviews.Add(review);
            await Db(scope).SaveChangesAsync();
            reviewId = review.Id;
        }

        (await _factory.CreateTenantClient()
            .PostAsJsonAsync($"/api/peer-reviews/{reviewId}/submit", new { score = 1 }))
            .EnsureSuccessStatusCode();

        var notif = Assert.Single(
            await NotificationsForAsync(author.Id), n => n.DedupeKey == $"pr-submit:{reviewId}");
        Assert.Equal(NotificationCategory.PeerReview, notif.Category);
        // THE blinding leak guard: neither the reviewer's display name nor email may appear.
        Assert.DoesNotContain(_factory.SeedUser.DisplayName, notif.Title);
        Assert.DoesNotContain(_factory.SeedUser.DisplayName, notif.Body);
        Assert.DoesNotContain(_factory.SeedUser.Email, notif.Title);
        Assert.DoesNotContain(_factory.SeedUser.Email, notif.Body);
    }

    [Fact]
    public async Task PeerReviewAssign_NotifiesReviewer()
    {
        var director = await SeedUserAsync(UserRole.MedicalDirector, "pr-director"); // PeerReviewManage
        var author = await SeedUserAsync(UserRole.Radiologist, "pr-assign-author");
        var reportId = await SeedReportAsync(author.Id);
        var reviewer = _factory.SeedUser; // Radiologist → PeerReviewSubmit, not the author

        var reviewId = await IdOfAsync(await ClientFor(director.Email).PostAsJsonAsync(
            "/api/peer-reviews", new { reportId, reviewerUserId = reviewer.Id }));

        var notif = Assert.Single(
            await NotificationsForAsync(reviewer.Id), n => n.DedupeKey == $"pr-assign:{reviewId}");
        Assert.Equal(NotificationCategory.PeerReview, notif.Category);
        Assert.Equal(NotificationUrgency.Warning, notif.Urgency);
    }

    // ── rulebooks ────────────────────────────────────────────────────────────

    [Fact]
    public async Task RulebookApprove_NotifiesManagers()
    {
        var manager = await SeedUserAsync(UserRole.ReportingAdmin, "rb-manager"); // RulebooksManage

        Guid rbId;
        using (var scope = NewScope())
        {
            var rb = new Rulebook
            {
                TenantId = _factory.SeedTenant.Id,
                RulebookId = $"rb-{Guid.NewGuid():N}",
                Name = "Test Rulebook",
                Version = "1.0",
                Owner = "owner",
                Status = RulebookStatus.Draft,
                SourceYaml = "",
                CompiledJson = "{}",
                AppliesToModalities = "",
                AppliesToBodyParts = "",
            };
            Db(scope).Rulebooks.Add(rb);
            await Db(scope).SaveChangesAsync();
            rbId = rb.Id;
        }

        // Approve as SeedAdmin (ItAdmin → RulebooksApprove) so the actor differs from the manager.
        (await _factory.CreateAdminClient().PostAsync($"/api/rulebooks/{rbId}/approve", null))
            .EnsureSuccessStatusCode();

        var notif = Assert.Single(
            await NotificationsForAsync(manager.Id), n => n.DedupeKey == $"rb-approve:{rbId}");
        Assert.Equal(NotificationCategory.RulebookApproval, notif.Category);
        Assert.Equal(NotificationUrgency.Info, notif.Urgency);
    }

    // ── templates ────────────────────────────────────────────────────────────

    private async Task<Guid> SeedTemplateAsync(TemplateStatus status)
    {
        using var scope = NewScope();
        var t = new ReportTemplate
        {
            TenantId = _factory.SeedTenant.Id,
            TemplateId = $"t-{Guid.NewGuid():N}",
            Name = "Test Template",
            Modality = "CT",
            BodyPart = "Chest",
            Subspecialty = "",
            SectionsJson = "[]",
            Status = status,
        };
        Db(scope).Templates.Add(t);
        await Db(scope).SaveChangesAsync();
        return t.Id;
    }

    [Fact]
    public async Task TemplateSubmitReview_NotifiesApprovers()
    {
        var approver = await SeedUserAsync(UserRole.ReportingAdmin, "tmpl-approver"); // TemplatesApprove
        var rowId = await SeedTemplateAsync(TemplateStatus.Draft);

        // Submit as SeedAdmin (ItAdmin → TemplatesManage), so the actor differs from the approver.
        (await _factory.CreateAdminClient().PostAsync($"/api/templates/{rowId}/submit-review", null))
            .EnsureSuccessStatusCode();

        var notif = Assert.Single(
            await NotificationsForAsync(approver.Id), n => n.DedupeKey == $"tmpl-submit:{rowId}");
        Assert.Equal(NotificationCategory.TemplateApproval, notif.Category);
    }

    [Fact]
    public async Task TemplateApprove_NotifiesSubmitter_ViaAuditLookup()
    {
        var submitter = await SeedUserAsync(UserRole.ReportingAdmin, "tmpl-submitter");
        var rowId = await SeedTemplateAsync(TemplateStatus.Review);

        // Seed the TemplateSubmittedForReview audit row the Approve handler mines for the submitter.
        using (var scope = NewScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
            await audit.AppendAsync(new AuditEvent
            {
                TenantId = _factory.SeedTenant.Id,
                UserId = submitter.Id,
                Action = AuditAction.TemplateSubmittedForReview,
                DetailsJson = JsonSerializer.Serialize(new { templateId = "seed", rowId }),
            }, CancellationToken.None);
        }

        // Approve as SeedAdmin (ItAdmin → TemplatesApprove), a different user from the submitter.
        (await _factory.CreateAdminClient().PostAsync($"/api/templates/{rowId}/approve", null))
            .EnsureSuccessStatusCode();

        var notif = Assert.Single(
            await NotificationsForAsync(submitter.Id), n => n.DedupeKey == $"tmpl-approve:{rowId}");
        Assert.Equal(NotificationCategory.TemplateApproval, notif.Category);
    }
}
