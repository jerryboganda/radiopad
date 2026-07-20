using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Security;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

/// <summary>
/// PRD §14.13 (PR-001..010) — RADPEER-aligned peer review &amp; quality.
///
/// Safety posture: a peer review is a QUALITY BENCHMARK, never a clinical
/// decision or an approval. Nothing here signs, unsigns, amends, or alters a
/// report — the only thing written is the review row and its audit trail.
///
/// Two invariants are enforced on every path and covered by tests:
/// 1. <b>No self-review.</b> A radiologist can never be assigned to peer-review
///    a report they authored or signed (PR-002 would be meaningless otherwise).
/// 2. <b>Blinding.</b> While a blinded assignment is still open, the reviewer's
///    projection omits the original author's id and name entirely, so the score
///    is given to the interpretation rather than to the colleague. Blinding
///    lifts the moment the reviewer submits.
///
/// Every query is filtered by the resolved tenant; every state change appends
/// to the append-only audit log via <see cref="IAuditLog.AppendAsync"/>.
/// </summary>
[ApiController]
[Route("api/peer-reviews")]
public class PeerReviewController : TenantedController
{
    /// <summary>ACR-style default sampling rate when a caller asks for a share rather than a count.</summary>
    private const double DefaultSampleRatePercent = 5.0;

    /// <summary>Upper bound on one sampling sweep so a mis-typed rate cannot flood the queue.</summary>
    private const int MaxSampleBatch = 200;

    private readonly RadioPadDbContext _db;
    private readonly IAuditLog _audit;

    public PeerReviewController(RadioPadDbContext db, IAuditLog audit)
    {
        _db = db;
        _audit = audit;
    }

    // ── Reviewer-facing reads ──────────────────────────────────────────────

    /// <summary>
    /// PR-002/PR-007 — the signed-in reviewer's own queue. Open assignments are
    /// returned with the author withheld; completed ones are unblinded so the
    /// reviewer can see whose work they scored.
    ///
    /// <paramref name="@as"/> = <c>author</c> flips the perspective to PR-008:
    /// the reviews recorded against MY OWN reports (attending feedback a
    /// resident needs to read, and the rows a radiologist may dispute). Only
    /// completed rows are returned there — an in-flight score is not feedback
    /// yet, and exposing it early would let the author lobby the reviewer.
    /// </summary>
    [HttpGet("mine")]
    public async Task<IActionResult> Mine(
        [FromQuery] string? status, [FromQuery(Name = "as")] string? @as, CancellationToken ct = default)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.PeerReviewRead);
        if (deny is not null) return deny;

        var asAuthor = string.Equals(@as, "author", StringComparison.OrdinalIgnoreCase);

        var query = asAuthor
            ? _db.PeerReviews.AsNoTracking().Where(p =>
                p.TenantId == tenant.Id
                && p.OriginalAuthorUserId == user.Id
                && (p.Status == PeerReviewStatus.Completed || p.Status == PeerReviewStatus.Disputed))
            : _db.PeerReviews.AsNoTracking().Where(p =>
                p.TenantId == tenant.Id && p.ReviewerUserId == user.Id);

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<PeerReviewStatus>(status, ignoreCase: true, out var wanted))
            query = query.Where(p => p.Status == wanted);

        var rows = await query.OrderBy(p => p.Status).ThenBy(p => p.CreatedAt).ToListAsync(ct);
        return Ok(await ProjectAsync(tenant, user, rows, ct));
    }

    /// <summary>
    /// The reviews recorded against one report. Deliberately does NOT 403 a
    /// caller who simply has no stake in the report — it returns only the rows
    /// they are entitled to see: their own assignments, reviews of their own
    /// report, or (with peer_review.manage) all of them.
    /// </summary>
    [HttpGet("report/{reportId:guid}")]
    public async Task<IActionResult> ForReport(Guid reportId, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.PeerReviewRead);
        if (deny is not null) return deny;

        var isProgrammeAdmin = RolePermissionMap.ForRole(user.Role).Contains(RbacPermission.PeerReviewManage);

        var rows = await _db.PeerReviews.AsNoTracking()
            .Where(p => p.TenantId == tenant.Id && p.ReportId == reportId)
            .Where(p => isProgrammeAdmin || p.ReviewerUserId == user.Id || p.OriginalAuthorUserId == user.Id)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(ct);

        return Ok(await ProjectAsync(tenant, user, rows, ct));
    }

    // ── Assignment (PR-001 / PR-002) ───────────────────────────────────────

    public record AssignDto(Guid ReportId, Guid ReviewerUserId, PeerReviewType? ReviewType, bool? Blinded);

    /// <summary>PR-002 — assign one report to a named peer reviewer.</summary>
    [HttpPost]
    public async Task<IActionResult> Assign([FromBody] AssignDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.PeerReviewManage);
        if (deny is not null) return deny;

        if (dto is null || dto.ReportId == Guid.Empty || dto.ReviewerUserId == Guid.Empty)
            return BadRequest(new { error = "reportId and reviewerUserId are required.", kind = "peer_review" });

        var report = await _db.Reports.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == dto.ReportId && r.TenantId == tenant.Id, ct);
        if (report is null)
            return NotFound(new { error = "Report not found in this tenant.", kind = "peer_review" });

        var reviewer = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == dto.ReviewerUserId && u.TenantId == tenant.Id, ct);
        if (reviewer is null)
            return NotFound(new { error = "Reviewer not found in this tenant.", kind = "peer_review" });
        if (!reviewer.IsActive)
            return BadRequest(new { error = "Reviewer has been deprovisioned.", kind = "peer_review" });
        if (!RolePermissionMap.ForRole(reviewer.Role).Contains(RbacPermission.PeerReviewSubmit))
            return BadRequest(new
            {
                error = $"Role '{reviewer.Role}' may not score peer reviews.",
                kind = "peer_review_reviewer_role",
            });

        var authorId = await ResolveAuthorAsync(tenant, report, ct);
        if (await IsSelfReviewAsync(tenant, report, authorId, reviewer.Id, ct))
            return BadRequest(new
            {
                error = "A radiologist cannot peer-review a report they authored or signed.",
                kind = "peer_review_self",
            });

        var alreadyOpen = await _db.PeerReviews.AsNoTracking().AnyAsync(
            p => p.TenantId == tenant.Id
                 && p.ReportId == report.Id
                 && p.ReviewerUserId == reviewer.Id
                 && (p.Status == PeerReviewStatus.Assigned || p.Status == PeerReviewStatus.InProgress),
            ct);
        if (alreadyOpen)
            return Conflict(new { error = "This reviewer already has an open review for that report.", kind = "peer_review" });

        var review = await CreateAsync(tenant, user, report.Id, reviewer.Id, authorId,
            dto.ReviewType ?? PeerReviewType.Targeted, dto.Blinded ?? true, ct);
        await _db.SaveChangesAsync(ct);
        await AuditAssignedAsync(tenant, user, review, ct);

        var projected = await ProjectAsync(tenant, user, new[] { review }, ct);
        return Ok(projected[0]);
    }

    public record SampleDto(int? Count, double? RatePercent, string? From, string? To, Guid[]? ReviewerUserIds);

    /// <summary>
    /// PR-001 — random selection. Picks N signed reports the tenant has not
    /// already peer-reviewed and spreads them across the eligible reviewer pool,
    /// never assigning a report back to its own author.
    /// </summary>
    [HttpPost("sample")]
    public async Task<IActionResult> Sample([FromBody] SampleDto? dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.PeerReviewManage);
        if (deny is not null) return deny;

        var from = ParseDate(dto?.From) ?? DateTimeOffset.UtcNow.AddDays(-30);
        var to = ParseDate(dto?.To) ?? DateTimeOffset.UtcNow;
        if (to < from)
            return BadRequest(new { error = "'to' must not precede 'from'.", kind = "peer_review" });

        // Eligible = signed inside the window, and not already in the queue.
        var signedReportIds = await _db.ReportSignatures.AsNoTracking()
            .Where(s => s.TenantId == tenant.Id && s.SignedAt >= from && s.SignedAt <= to)
            .Select(s => s.ReportId)
            .Distinct()
            .ToListAsync(ct);

        var alreadyQueued = await _db.PeerReviews.AsNoTracking()
            .Where(p => p.TenantId == tenant.Id && signedReportIds.Contains(p.ReportId))
            .Select(p => p.ReportId)
            .Distinct()
            .ToListAsync(ct);

        var candidateIds = signedReportIds.Except(alreadyQueued).ToList();
        var candidates = await _db.Reports.AsNoTracking()
            .Where(r => r.TenantId == tenant.Id && candidateIds.Contains(r.Id))
            .ToListAsync(ct);

        var reviewers = await ResolveReviewerPoolAsync(tenant, dto?.ReviewerUserIds, ct);
        if (reviewers.Count == 0)
            return BadRequest(new
            {
                error = "No eligible peer reviewers in this tenant.",
                kind = "peer_review_no_reviewers",
            });

        var target = ResolveSampleSize(dto, candidates.Count);
        var shuffled = Shuffle(candidates);

        var created = new List<PeerReview>();
        var skippedNoReviewer = 0;
        foreach (var report in shuffled)
        {
            if (created.Count >= target) break;

            var authorId = await ResolveAuthorAsync(tenant, report, ct);
            // PR-002 + the no-self-review invariant: only reviewers who neither
            // authored nor signed this study are eligible for it.
            var eligible = new List<User>();
            foreach (var candidate in reviewers)
                if (!await IsSelfReviewAsync(tenant, report, authorId, candidate.Id, ct))
                    eligible.Add(candidate);

            if (eligible.Count == 0)
            {
                skippedNoReviewer++;
                continue;
            }

            var reviewer = eligible[Random.Shared.Next(eligible.Count)];
            created.Add(await CreateAsync(tenant, user, report.Id, reviewer.Id, authorId,
                PeerReviewType.Random, blinded: true, ct));
        }

        if (created.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
            foreach (var review in created)
                await AuditAssignedAsync(tenant, user, review, ct);
        }

        return Ok(new
        {
            assigned = created.Count,
            eligible = candidates.Count,
            requested = target,
            skippedNoEligibleReviewer = skippedNoReviewer,
            reviews = await ProjectAsync(tenant, user, created, ct),
        });
    }

    // ── Reviewer actions (PR-003) ──────────────────────────────────────────

    /// <summary>Marks an assignment as opened, so the queue distinguishes untouched from in-flight work.</summary>
    [HttpPost("{id:guid}/start")]
    public async Task<IActionResult> Start(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.PeerReviewSubmit);
        if (deny is not null) return deny;

        var review = await LoadForReviewerAsync(tenant, user, id, ct);
        if (review is null) return NotFound(new { error = "Peer review not found.", kind = "peer_review" });
        if (review.ReviewerUserId != user.Id) return NotAssignedToYou();
        if (review.Status is PeerReviewStatus.Completed or PeerReviewStatus.Disputed)
            return Conflict(new { error = "This review has already been submitted.", kind = "peer_review" });

        if (review.Status == PeerReviewStatus.Assigned)
        {
            review.Status = PeerReviewStatus.InProgress;
            review.StartedAt = DateTimeOffset.UtcNow;
            review.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        var projected = await ProjectAsync(tenant, user, new[] { review }, ct);
        return Ok(projected[0]);
    }

    public record SubmitDto(
        int Score,
        PeerReviewDiscrepancyCategory? DiscrepancyCategory,
        PeerReviewComplexity? Complexity,
        string? Comments);

    /// <summary>
    /// PR-003 — record the RADPEER score plus structured rationale. Only the
    /// assigned reviewer may submit, and only once; submitting unblinds the
    /// original author to that reviewer.
    /// </summary>
    [HttpPost("{id:guid}/submit")]
    public async Task<IActionResult> Submit(Guid id, [FromBody] SubmitDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.PeerReviewSubmit);
        if (deny is not null) return deny;

        if (dto is null || dto.Score is < 1 or > 4)
            return BadRequest(new
            {
                error = "score must be a RADPEER value between 1 (concur) and 4 (discrepancy that should be made almost every time).",
                kind = "peer_review_score",
            });

        var review = await LoadForReviewerAsync(tenant, user, id, ct);
        if (review is null) return NotFound(new { error = "Peer review not found.", kind = "peer_review" });
        if (review.ReviewerUserId != user.Id) return NotAssignedToYou();
        if (review.Status is PeerReviewStatus.Completed or PeerReviewStatus.Disputed)
            return Conflict(new { error = "This review has already been submitted.", kind = "peer_review" });

        var score = (PeerReviewScore)dto.Score;
        var category = dto.DiscrepancyCategory ?? PeerReviewDiscrepancyCategory.None;

        // Structured rationale must agree with the score: a discrepancy has to say
        // WHERE it came from (PR-009 analyses by category), and a concur cannot
        // carry one.
        if (score == PeerReviewScore.Concur && category != PeerReviewDiscrepancyCategory.None)
            return BadRequest(new
            {
                error = "A concurring review cannot carry a discrepancy category.",
                kind = "peer_review_rationale",
            });
        if (score != PeerReviewScore.Concur && category == PeerReviewDiscrepancyCategory.None)
            return BadRequest(new
            {
                error = "A discrepancy score requires a discrepancy category (perceptual, interpretive, communication, or technique).",
                kind = "peer_review_rationale",
            });

        review.Score = score;
        review.DiscrepancyCategory = category;
        review.Complexity = dto.Complexity ?? PeerReviewComplexity.Routine;
        review.Comments = (dto.Comments ?? "").Trim();
        review.Status = PeerReviewStatus.Completed;
        review.StartedAt ??= DateTimeOffset.UtcNow;
        review.CompletedAt = DateTimeOffset.UtcNow;
        review.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        // Audit records the structured verdict only — the free-text rationale may
        // quote clinical narrative, so it stays in the row and out of the log.
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            ReportId = review.ReportId,
            Action = AuditAction.PeerReviewSubmitted,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                peerReviewId = review.Id,
                score = review.Score.ToString(),
                complexity = review.Complexity.ToString(),
                discrepancyCategory = review.DiscrepancyCategory.ToString(),
                reviewType = review.ReviewType.ToString(),
            }),
        }, ct);

        var projected = await ProjectAsync(tenant, user, new[] { review }, ct);
        return Ok(projected[0]);
    }

    public record DisputeDto(string? Reason);

    /// <summary>The original author contests a completed score; a director adjudicates out of band.</summary>
    [HttpPost("{id:guid}/dispute")]
    public async Task<IActionResult> Dispute(Guid id, [FromBody] DisputeDto? dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.PeerReviewSubmit);
        if (deny is not null) return deny;

        var review = await _db.PeerReviews
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenant.Id, ct);
        if (review is null) return NotFound(new { error = "Peer review not found.", kind = "peer_review" });
        if (review.OriginalAuthorUserId != user.Id)
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "Only the radiologist whose report was reviewed may dispute it.",
                kind = "peer_review_not_author",
            });
        if (review.Status != PeerReviewStatus.Completed)
            return Conflict(new { error = "Only a completed review can be disputed.", kind = "peer_review" });

        var reason = (dto?.Reason ?? "").Trim();
        if (reason.Length == 0)
            return BadRequest(new { error = "A dispute must state a reason.", kind = "peer_review" });

        review.Status = PeerReviewStatus.Disputed;
        review.DisputeReason = reason;
        review.DisputedAt = DateTimeOffset.UtcNow;
        review.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            ReportId = review.ReportId,
            Action = AuditAction.PeerReviewDisputed,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                peerReviewId = review.Id,
                score = review.Score.ToString(),
            }),
        }, ct);

        var projected = await ProjectAsync(tenant, user, new[] { review }, ct);
        return Ok(projected[0]);
    }

    // ── Quality dashboard (PR-005 / PR-009) ────────────────────────────────

    /// <summary>
    /// PR-005/PR-009 — per-radiologist concordance and discrepancy breakdown
    /// over a date range. Director/quality-admin only: this is the one view that
    /// names readers next to their discrepancy rate.
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> Stats([FromQuery] string? from, [FromQuery] string? to, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.PeerReviewManage);
        if (deny is not null) return deny;

        var fromAt = ParseDate(from) ?? DateTimeOffset.UtcNow.AddDays(-90);
        var toAt = ParseDate(to) ?? DateTimeOffset.UtcNow;
        if (toAt < fromAt)
            return BadRequest(new { error = "'to' must not precede 'from'.", kind = "peer_review" });

        var scored = await _db.PeerReviews.AsNoTracking()
            .Where(p => p.TenantId == tenant.Id
                        && p.Score != PeerReviewScore.NotScored
                        && p.CompletedAt != null
                        && p.CompletedAt >= fromAt
                        && p.CompletedAt <= toAt)
            .ToListAsync(ct);

        var names = await NameMapAsync(tenant, scored
            .Select(p => p.OriginalAuthorUserId)
            .Distinct()
            .ToList(), ct);

        var perReader = scored
            .GroupBy(p => p.OriginalAuthorUserId)
            .Select(g =>
            {
                var total = g.Count();
                var concur = g.Count(p => p.Score == PeerReviewScore.Concur);
                return new
                {
                    userId = g.Key,
                    displayName = names.TryGetValue(g.Key, out var n) ? n : "Unknown",
                    reviewed = total,
                    concur,
                    discrepancies = total - concur,
                    // Concordance = share of reviews scored RADPEER 1. Reported as a
                    // benchmark (PRD §22) — deliberately NOT a target to minimise.
                    concordanceRate = total == 0 ? 0d : Math.Round((double)concur / total, 4),
                    byScore = new
                    {
                        concur,
                        minor = g.Count(p => p.Score == PeerReviewScore.DiscrepancyUnlikelySignificant),
                        moderate = g.Count(p => p.Score == PeerReviewScore.DiscrepancyShouldBeMadeMostOfTheTime),
                        major = g.Count(p => p.Score == PeerReviewScore.DiscrepancyShouldBeMadeAlmostEveryTime),
                    },
                    byCategory = CategoryCounts(g),
                    complexCases = g.Count(p => p.Complexity == PeerReviewComplexity.Complex),
                    disputed = g.Count(p => p.Status == PeerReviewStatus.Disputed),
                };
            })
            .OrderByDescending(r => r.reviewed)
            .ToList();

        var overallTotal = scored.Count;
        var overallConcur = scored.Count(p => p.Score == PeerReviewScore.Concur);

        var pending = await _db.PeerReviews.AsNoTracking()
            .CountAsync(p => p.TenantId == tenant.Id
                             && (p.Status == PeerReviewStatus.Assigned || p.Status == PeerReviewStatus.InProgress), ct);

        return Ok(new
        {
            from = fromAt,
            to = toAt,
            totals = new
            {
                reviewed = overallTotal,
                concur = overallConcur,
                discrepancies = overallTotal - overallConcur,
                concordanceRate = overallTotal == 0 ? 0d : Math.Round((double)overallConcur / overallTotal, 4),
                pending,
                byCategory = CategoryCounts(scored),
            },
            perReader,
        });
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>A reviewer may only act on their OWN assignment, never a colleague's.</summary>
    private IActionResult NotAssignedToYou() =>
        StatusCode(StatusCodes.Status403Forbidden, new
        {
            error = "This peer review is assigned to another reviewer.",
            kind = "peer_review_not_reviewer",
        });

    private static object CategoryCounts(IEnumerable<PeerReview> reviews)
    {
        var list = reviews as ICollection<PeerReview> ?? reviews.ToList();
        return new
        {
            perceptual = list.Count(p => p.DiscrepancyCategory == PeerReviewDiscrepancyCategory.Perceptual),
            interpretive = list.Count(p => p.DiscrepancyCategory == PeerReviewDiscrepancyCategory.Interpretive),
            communication = list.Count(p => p.DiscrepancyCategory == PeerReviewDiscrepancyCategory.Communication),
            technique = list.Count(p => p.DiscrepancyCategory == PeerReviewDiscrepancyCategory.Technique),
        };
    }

    private Task<PeerReview?> LoadForReviewerAsync(Tenant tenant, User user, Guid id, CancellationToken ct) =>
        _db.PeerReviews.FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenant.Id, ct);

    private async Task<PeerReview> CreateAsync(
        Tenant tenant, User assignedBy, Guid reportId, Guid reviewerId, Guid authorId,
        PeerReviewType type, bool blinded, CancellationToken ct)
    {
        var review = new PeerReview
        {
            TenantId = tenant.Id,
            ReportId = reportId,
            ReviewerUserId = reviewerId,
            OriginalAuthorUserId = authorId,
            AssignedByUserId = assignedBy.Id,
            ReviewType = type,
            Status = PeerReviewStatus.Assigned,
            Blinded = blinded,
        };
        await _db.PeerReviews.AddAsync(review, ct);
        return review;
    }

    private Task AuditAssignedAsync(Tenant tenant, User actor, PeerReview review, CancellationToken ct) =>
        _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = actor.Id,
            ReportId = review.ReportId,
            Action = AuditAction.PeerReviewAssigned,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                peerReviewId = review.Id,
                reviewerUserId = review.ReviewerUserId,
                reviewType = review.ReviewType.ToString(),
                blinded = review.Blinded,
            }),
        }, ct);

    /// <summary>
    /// The reader under review: the report's author, unless a different
    /// radiologist put the primary signature on it (the signer owns the
    /// interpretation for RADPEER purposes).
    /// </summary>
    private async Task<Guid> ResolveAuthorAsync(Tenant tenant, Report report, CancellationToken ct)
    {
        var primary = await _db.ReportSignatures.AsNoTracking()
            .Where(s => s.TenantId == tenant.Id && s.ReportId == report.Id && s.Role == SignatureRole.Primary)
            .OrderBy(s => s.SignedAt)
            .Select(s => (Guid?)s.UserId)
            .FirstOrDefaultAsync(ct);
        return primary ?? report.CreatedByUserId;
    }

    /// <summary>
    /// True when assigning <paramref name="reviewerId"/> would be a self-review:
    /// they authored the report, they are the resolved reader, or they signed it
    /// in any capacity (primary, co-signer, addendum).
    /// </summary>
    private async Task<bool> IsSelfReviewAsync(
        Tenant tenant, Report report, Guid authorId, Guid reviewerId, CancellationToken ct)
    {
        if (reviewerId == authorId || reviewerId == report.CreatedByUserId) return true;
        return await _db.ReportSignatures.AsNoTracking()
            .AnyAsync(s => s.TenantId == tenant.Id && s.ReportId == report.Id && s.UserId == reviewerId, ct);
    }

    private async Task<List<User>> ResolveReviewerPoolAsync(Tenant tenant, Guid[]? explicitIds, CancellationToken ct)
    {
        var query = _db.Users.AsNoTracking().Where(u => u.TenantId == tenant.Id && u.IsActive);
        if (explicitIds is { Length: > 0 })
        {
            var wanted = explicitIds.ToList();
            query = query.Where(u => wanted.Contains(u.Id));
        }

        var users = await query.ToListAsync(ct);
        return users
            .Where(u => RolePermissionMap.ForRole(u.Role).Contains(RbacPermission.PeerReviewSubmit))
            .ToList();
    }

    private static int ResolveSampleSize(SampleDto? dto, int eligible)
    {
        if (dto?.Count is > 0) return Math.Min(dto.Count.Value, MaxSampleBatch);
        var rate = dto?.RatePercent is > 0 ? dto.RatePercent!.Value : DefaultSampleRatePercent;
        var n = (int)Math.Ceiling(eligible * rate / 100.0);
        return Math.Clamp(n, 0, MaxSampleBatch);
    }

    private static List<T> Shuffle<T>(List<T> source)
    {
        var copy = source.ToList();
        for (var i = copy.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }
        return copy;
    }

    private static DateTimeOffset? ParseDate(string? raw) =>
        DateTimeOffset.TryParse(raw, out var parsed) ? parsed : null;

    private async Task<Dictionary<Guid, string>> NameMapAsync(Tenant tenant, List<Guid> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0) return new Dictionary<Guid, string>();
        return await _db.Users.AsNoTracking()
            .Where(u => u.TenantId == tenant.Id && userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => string.IsNullOrWhiteSpace(u.DisplayName) ? u.Email : u.DisplayName, ct);
    }

    /// <summary>
    /// PR-002 — the ONLY projection used by every read path. While a blinded
    /// assignment is still open AND the caller is the reviewer, the author id
    /// and name are omitted from the payload entirely (not blanked, not sent as
    /// a placeholder id) so no client can reconstruct them.
    /// </summary>
    private async Task<List<Dictionary<string, object?>>> ProjectAsync(
        Tenant tenant, User caller, IReadOnlyCollection<PeerReview> rows, CancellationToken ct)
    {
        var result = new List<Dictionary<string, object?>>();
        if (rows.Count == 0) return result;

        var reportIds = rows.Select(r => r.ReportId).Distinct().ToList();
        var reports = await _db.Reports.AsNoTracking()
            .Where(r => r.TenantId == tenant.Id && reportIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, ct);

        var names = await NameMapAsync(tenant, rows
            .SelectMany(r => new[] { r.ReviewerUserId, r.OriginalAuthorUserId })
            .Distinct()
            .ToList(), ct);

        foreach (var row in rows)
        {
            // Blinding protects the reviewer's judgement from the author's identity.
            // It never hides the author from the author, nor from the programme
            // administrator who has to read the concordance dashboard.
            var hideAuthor = row.Blinded
                             && !row.IsUnblinded
                             && caller.Id == row.ReviewerUserId;

            var item = new Dictionary<string, object?>
            {
                ["id"] = row.Id,
                ["reportId"] = row.ReportId,
                ["reviewerUserId"] = row.ReviewerUserId,
                ["reviewerName"] = names.TryGetValue(row.ReviewerUserId, out var rn) ? rn : null,
                ["reviewType"] = row.ReviewType.ToString(),
                ["status"] = row.Status.ToString(),
                ["score"] = (int)row.Score,
                ["scoreName"] = row.Score.ToString(),
                ["complexity"] = row.Complexity.ToString(),
                ["discrepancyCategory"] = row.DiscrepancyCategory.ToString(),
                ["comments"] = row.Comments,
                ["blinded"] = row.Blinded,
                ["authorHidden"] = hideAuthor,
                ["startedAt"] = row.StartedAt,
                ["completedAt"] = row.CompletedAt,
                ["disputeReason"] = row.DisputeReason,
                ["disputedAt"] = row.DisputedAt,
                ["createdAt"] = row.CreatedAt,
            };

            if (!hideAuthor)
            {
                item["originalAuthorUserId"] = row.OriginalAuthorUserId;
                item["originalAuthorName"] =
                    names.TryGetValue(row.OriginalAuthorUserId, out var an) ? an : null;
            }

            if (reports.TryGetValue(row.ReportId, out var report))
            {
                item["study"] = new
                {
                    accessionNumber = report.Study.AccessionNumber,
                    modality = report.Study.Modality,
                    bodyPart = report.Study.BodyPart,
                };
            }

            result.Add(item);
        }

        return result;
    }
}
