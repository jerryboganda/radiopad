using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// PRD §14.13 (PR-001..010) — peer review &amp; quality. Covers the happy path
/// (assign → submit → stats), and the three invariants the module is worthless
/// without: no self-review, no cross-tenant leakage, and reviewer blinding that
/// actually withholds the author until the score is in.
/// </summary>
public class PeerReviewTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;

    public PeerReviewTests(RadioPadAppFactory factory) => _factory = factory;

    // ── Happy path ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Assign_Submit_Stats_HappyPath()
    {
        var w = await SetupAsync();

        var director = ClientFor(w.Director.Email);
        var assign = await director.PostAsJsonAsync("/api/peer-reviews", new
        {
            reportId = w.ReportId,
            reviewerUserId = w.Reviewer.Id,
            reviewType = 1, // Targeted
            blinded = true,
        });
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);
        var review = await assign.Content.ReadFromJsonAsync<JsonElement>();
        var reviewId = review.GetProperty("id").GetGuid();
        Assert.Equal("Assigned", review.GetProperty("status").GetString());

        // The reviewer sees exactly one open assignment in their own queue.
        var reviewer = ClientFor(w.Reviewer.Email);
        var mine = await reviewer.GetFromJsonAsync<JsonElement>("/api/peer-reviews/mine");
        Assert.Equal(1, mine.GetArrayLength());
        Assert.Equal(reviewId, mine[0].GetProperty("id").GetGuid());

        // Opening it moves Assigned → InProgress without recording any score.
        var start = await reviewer.PostAsync($"/api/peer-reviews/{reviewId}/start", null);
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var started = await start.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("InProgress", started.GetProperty("status").GetString());
        Assert.Equal(0, started.GetProperty("score").GetInt32());

        // RADPEER 3 with an interpretive rationale.
        var submit = await reviewer.PostAsJsonAsync($"/api/peer-reviews/{reviewId}/submit", new
        {
            score = 3,
            discrepancyCategory = 2, // Interpretive
            complexity = 1,          // Complex
            comments = "Subtle nodule characterised as benign.",
        });
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
        var submitted = await submit.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Completed", submitted.GetProperty("status").GetString());
        Assert.Equal(3, submitted.GetProperty("score").GetInt32());
        Assert.Equal("Interpretive", submitted.GetProperty("discrepancyCategory").GetString());
        Assert.Equal("Complex", submitted.GetProperty("complexity").GetString());

        // Stats attribute the discrepancy to the ORIGINAL AUTHOR, not the reviewer.
        // Asserted on this test's own reader row: the tenant-wide totals also carry
        // whatever sibling tests in this class recorded.
        var stats = await director.GetFromJsonAsync<JsonElement>("/api/peer-reviews/stats");
        var row = stats.GetProperty("perReader")
            .EnumerateArray()
            .Single(r => r.GetProperty("userId").GetGuid() == w.Author.Id);
        Assert.Equal(w.Author.DisplayName, row.GetProperty("displayName").GetString());
        Assert.Equal(1, row.GetProperty("reviewed").GetInt32());
        Assert.Equal(0, row.GetProperty("concur").GetInt32());
        Assert.Equal(1, row.GetProperty("discrepancies").GetInt32());
        Assert.Equal(0d, row.GetProperty("concordanceRate").GetDouble());
        Assert.Equal(1, row.GetProperty("byScore").GetProperty("moderate").GetInt32());
        Assert.Equal(1, row.GetProperty("byCategory").GetProperty("interpretive").GetInt32());
        Assert.Equal(1, row.GetProperty("complexCases").GetInt32());
        // The reviewer is never scored for someone else's report.
        Assert.DoesNotContain(
            stats.GetProperty("perReader").EnumerateArray(),
            r => r.GetProperty("userId").GetGuid() == w.Reviewer.Id);

        // Every state change is on the append-only audit log.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var actions = await db.AuditEvents.AsNoTracking()
            .Where(a => a.TenantId == _factory.SeedTenant.Id && a.ReportId == w.ReportId)
            .Select(a => a.Action)
            .ToListAsync();
        Assert.Contains(AuditAction.PeerReviewAssigned, actions);
        Assert.Contains(AuditAction.PeerReviewSubmitted, actions);
    }

    // ── Invariant 1: never review your own work ────────────────────────────

    [Fact]
    public async Task Assign_RejectsSelfReview_ForTheReportAuthor()
    {
        var w = await SetupAsync();
        var director = ClientFor(w.Director.Email);

        var resp = await director.PostAsJsonAsync("/api/peer-reviews", new
        {
            reportId = w.ReportId,
            reviewerUserId = w.Author.Id, // the author of the report under review
            reviewType = 1,
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("peer_review_self", body.GetProperty("kind").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        Assert.False(await db.PeerReviews.AnyAsync(p => p.ReportId == w.ReportId));
    }

    [Fact]
    public async Task Assign_RejectsSelfReview_WhenTheReviewerSignedTheReport()
    {
        // A co-signer never authored the draft, but they put their name on the
        // interpretation — peer-reviewing it would still be self-review.
        var w = await SetupAsync();
        var cosigner = await AddUserAsync(_factory.SeedTenant.Id, UserRole.Radiologist);
        await AddSignatureAsync(_factory.SeedTenant.Id, w.ReportId, cosigner.Id, SignatureRole.CoSigner);

        var resp = await ClientFor(w.Director.Email).PostAsJsonAsync("/api/peer-reviews", new
        {
            reportId = w.ReportId,
            reviewerUserId = cosigner.Id,
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("peer_review_self", body.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Sample_NeverAssignsAReportBackToItsOwnAuthor()
    {
        var w = await SetupAsync(reportCount: 6);
        var director = ClientFor(w.Director.Email);

        var resp = await director.PostAsJsonAsync("/api/peer-reviews/sample", new
        {
            count = 6,
            reviewerUserIds = new[] { w.Reviewer.Id, w.Author.Id },
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("assigned").GetInt32() > 0);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var created = await db.PeerReviews.AsNoTracking()
            .Where(p => p.TenantId == _factory.SeedTenant.Id && p.ReviewType == PeerReviewType.Random)
            .ToListAsync();

        Assert.NotEmpty(created);
        Assert.All(created, p => Assert.NotEqual(p.OriginalAuthorUserId, p.ReviewerUserId));
        // Even with the author explicitly in the reviewer pool, they never draw
        // their own study — the sampler falls back to the other eligible reader.
        Assert.All(
            created.Where(p => p.OriginalAuthorUserId == w.Author.Id),
            p => Assert.Equal(w.Reviewer.Id, p.ReviewerUserId));
    }

    // ── Invariant 2: blinding ──────────────────────────────────────────────

    [Fact]
    public async Task Blinding_WithholdsAuthorIdentityFromTheReviewerUntilSubmitted()
    {
        var w = await SetupAsync();
        var reviewId = await AssignAsync(w, blinded: true);
        var reviewer = ClientFor(w.Reviewer.Email);

        var open = await reviewer.GetFromJsonAsync<JsonElement>("/api/peer-reviews/mine");
        var row = open[0];
        Assert.True(row.GetProperty("authorHidden").GetBoolean());
        // Absent outright — not blanked, not a placeholder id the client could resolve.
        Assert.False(row.TryGetProperty("originalAuthorUserId", out _));
        Assert.False(row.TryGetProperty("originalAuthorName", out _));

        // The raw payload must not carry the author's id or display name anywhere.
        var raw = await reviewer.GetStringAsync("/api/peer-reviews/mine");
        Assert.DoesNotContain(w.Author.Id.ToString(), raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(w.Author.DisplayName, raw, StringComparison.OrdinalIgnoreCase);

        await reviewer.PostAsJsonAsync($"/api/peer-reviews/{reviewId}/submit", new
        {
            score = 1,
            discrepancyCategory = 0,
        });

        // Submitting lifts the blind.
        var after = await reviewer.GetFromJsonAsync<JsonElement>("/api/peer-reviews/mine");
        var done = after[0];
        Assert.False(done.GetProperty("authorHidden").GetBoolean());
        Assert.Equal(w.Author.Id, done.GetProperty("originalAuthorUserId").GetGuid());
        Assert.Equal(w.Author.DisplayName, done.GetProperty("originalAuthorName").GetString());
    }

    [Fact]
    public async Task Blinding_DoesNotHideTheAuthorFromTheProgrammeAdministrator()
    {
        var w = await SetupAsync();
        await AssignAsync(w, blinded: true);

        var director = ClientFor(w.Director.Email);
        var rows = await director.GetFromJsonAsync<JsonElement>($"/api/peer-reviews/report/{w.ReportId}");

        Assert.Equal(1, rows.GetArrayLength());
        Assert.False(rows[0].GetProperty("authorHidden").GetBoolean());
        Assert.Equal(w.Author.Id, rows[0].GetProperty("originalAuthorUserId").GetGuid());
    }

    // ── Invariant 3: tenant isolation ──────────────────────────────────────

    [Fact]
    public async Task TenantIsolation_KeepsPeerReviewsInsideTheirOwnTenant()
    {
        var w = await SetupAsync();
        await AssignAsync(w, blinded: true);

        var (otherTenant, otherDirector, otherReviewer) = await SetupOtherTenantAsync();
        var intruder = ClientFor(otherDirector.Email, otherTenant.Slug);

        // The other tenant's report is invisible: the review list is empty...
        var rows = await intruder.GetFromJsonAsync<JsonElement>($"/api/peer-reviews/report/{w.ReportId}");
        Assert.Equal(0, rows.GetArrayLength());

        // ...their stats count nothing from tenant A...
        var stats = await intruder.GetFromJsonAsync<JsonElement>("/api/peer-reviews/stats");
        Assert.Equal(0, stats.GetProperty("totals").GetProperty("reviewed").GetInt32());
        Assert.Equal(0, stats.GetProperty("totals").GetProperty("pending").GetInt32());

        // ...and they cannot assign tenant A's report to one of their own readers.
        var assign = await intruder.PostAsJsonAsync("/api/peer-reviews", new
        {
            reportId = w.ReportId,
            reviewerUserId = otherReviewer.Id,
        });
        Assert.Equal(HttpStatusCode.NotFound, assign.StatusCode);

        // A reviewer in the other tenant has an empty queue.
        var mine = await ClientFor(otherReviewer.Email, otherTenant.Slug)
            .GetFromJsonAsync<JsonElement>("/api/peer-reviews/mine");
        Assert.Equal(0, mine.GetArrayLength());
    }

    // ── Guard rails ────────────────────────────────────────────────────────

    [Fact]
    public async Task Submit_IsRejectedForAReviewAssignedToSomeoneElse()
    {
        var w = await SetupAsync();
        var reviewId = await AssignAsync(w, blinded: true);
        var interloper = await AddUserAsync(_factory.SeedTenant.Id, UserRole.Radiologist);

        var resp = await ClientFor(interloper.Email)
            .PostAsJsonAsync($"/api/peer-reviews/{reviewId}/submit", new { score = 1, discrepancyCategory = 0 });

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("peer_review_not_reviewer", body.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Submit_RequiresARationaleThatMatchesTheScore()
    {
        var w = await SetupAsync();
        var reviewId = await AssignAsync(w, blinded: true);
        var reviewer = ClientFor(w.Reviewer.Email);

        // Discrepancy without a category.
        var noCategory = await reviewer.PostAsJsonAsync(
            $"/api/peer-reviews/{reviewId}/submit", new { score = 4, discrepancyCategory = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, noCategory.StatusCode);

        // Concur WITH a category.
        var contradictory = await reviewer.PostAsJsonAsync(
            $"/api/peer-reviews/{reviewId}/submit", new { score = 1, discrepancyCategory = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, contradictory.StatusCode);

        // Out-of-range RADPEER score.
        var outOfRange = await reviewer.PostAsJsonAsync(
            $"/api/peer-reviews/{reviewId}/submit", new { score = 7, discrepancyCategory = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, outOfRange.StatusCode);
    }

    [Fact]
    public async Task Stats_AreForbiddenToAPlainRadiologist()
    {
        var w = await SetupAsync();
        var resp = await ClientFor(w.Reviewer.Email).GetAsync("/api/peer-reviews/stats");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Dispute_IsReservedForTheReviewedAuthor()
    {
        var w = await SetupAsync();
        var reviewId = await AssignAsync(w, blinded: true);
        var reviewer = ClientFor(w.Reviewer.Email);
        await reviewer.PostAsJsonAsync($"/api/peer-reviews/{reviewId}/submit",
            new { score = 3, discrepancyCategory = 1 });

        // The reviewer cannot dispute their own score.
        var wrongUser = await reviewer.PostAsJsonAsync(
            $"/api/peer-reviews/{reviewId}/dispute", new { reason = "I changed my mind" });
        Assert.Equal(HttpStatusCode.Forbidden, wrongUser.StatusCode);

        // The author can.
        var author = ClientFor(w.Author.Email);
        var ok = await author.PostAsJsonAsync(
            $"/api/peer-reviews/{reviewId}/dispute", new { reason = "Prior study supports the original read." });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var body = await ok.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Disputed", body.GetProperty("status").GetString());
    }

    // ── Fixtures ───────────────────────────────────────────────────────────

    private sealed record World(User Author, User Reviewer, User Director, Guid ReportId);

    /// <summary>
    /// Builds an isolated cast (author / reviewer / director) plus
    /// <paramref name="reportCount"/> signed reports authored by the author, so
    /// each test starts from data no other test in the class can perturb.
    /// </summary>
    private async Task<World> SetupAsync(int reportCount = 1)
    {
        var tenantId = _factory.SeedTenant.Id;
        var author = await AddUserAsync(tenantId, UserRole.Radiologist);
        var reviewer = await AddUserAsync(tenantId, UserRole.Radiologist);
        var director = await AddUserAsync(tenantId, UserRole.MedicalDirector);

        Guid firstReport = Guid.Empty;
        for (var i = 0; i < reportCount; i++)
        {
            var id = await AddSignedReportAsync(tenantId, author.Id);
            if (i == 0) firstReport = id;
        }

        return new World(author, reviewer, director, firstReport);
    }

    private async Task<(Tenant tenant, User director, User reviewer)> SetupOtherTenantAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();

        var tenant = new Tenant { Slug = $"pr-other-{Guid.NewGuid():N}"[..20], DisplayName = "Other" };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var director = await AddUserAsync(tenant.Id, UserRole.MedicalDirector);
        var reviewer = await AddUserAsync(tenant.Id, UserRole.Radiologist);
        return (tenant, director, reviewer);
    }

    private async Task<User> AddUserAsync(Guid tenantId, UserRole role)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var user = new User
        {
            TenantId = tenantId,
            Email = $"pr-{role.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}@radiopad.local",
            DisplayName = $"PR {role} {Guid.NewGuid():N}"[..24],
            Role = role,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private async Task<Guid> AddSignedReportAsync(Guid tenantId, Guid authorId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var report = new Report
        {
            TenantId = tenantId,
            CreatedByUserId = authorId,
            Status = ReportStatus.Exported,
            Study = new StudyContext
            {
                AccessionNumber = $"ACC-{Guid.NewGuid():N}"[..12],
                Modality = "CT",
                BodyPart = "Chest",
            },
            Findings = "No acute intracranial abnormality.",
            Impression = "Normal study.",
        };
        db.Reports.Add(report);
        db.ReportSignatures.Add(new ReportSignature
        {
            TenantId = tenantId,
            ReportId = report.Id,
            UserId = authorId,
            Role = SignatureRole.Primary,
            SignedAt = DateTimeOffset.UtcNow.AddHours(-1),
        });
        await db.SaveChangesAsync();
        return report.Id;
    }

    private async Task AddSignatureAsync(Guid tenantId, Guid reportId, Guid userId, SignatureRole role)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        db.ReportSignatures.Add(new ReportSignature
        {
            TenantId = tenantId,
            ReportId = reportId,
            UserId = userId,
            Role = role,
            SignedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> AssignAsync(World w, bool blinded)
    {
        var resp = await ClientFor(w.Director.Email).PostAsJsonAsync("/api/peer-reviews", new
        {
            reportId = w.ReportId,
            reviewerUserId = w.Reviewer.Id,
            reviewType = 1,
            blinded,
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    private HttpClient ClientFor(string email, string? tenantSlug = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-RadioPad-Tenant", tenantSlug ?? _factory.SeedTenant.Slug);
        client.DefaultRequestHeaders.Add("X-RadioPad-User", email);
        return client;
    }
}
