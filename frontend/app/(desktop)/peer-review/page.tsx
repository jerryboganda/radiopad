'use client';

/**
 * PRD §14.13 (PR-001..010) — Peer Review & Quality.
 *
 * Three stacked surfaces, each gated by what the signed-in user may actually do:
 *  1. "Cases to review" — my open assignments (PR-007). While an assignment is
 *     blinded the server omits the author entirely, so this screen shows an
 *     explicit "author hidden" state rather than an empty name.
 *  2. The scoring form (PR-003) — RADPEER 1–4 with plain-English labels plus a
 *     structured discrepancy category and free-text rationale.
 *  3. "Feedback on my reports" (PR-008) and the concordance dashboard (PR-005),
 *     the latter only for holders of `peer_review.manage`.
 *
 * Nothing here signs, unsigns, or edits a report — a peer review is a quality
 * record, never a clinical approval.
 */

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { UsersRound, EyeOff, ShieldQuestion, ArrowRight } from 'lucide-react';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import { TableSkeleton } from '@/components/ui/Skeleton';
import EmptyState from '@/components/ui/EmptyState';
import ErrorState from '@/components/ui/ErrorState';
import Banner from '@/components/ui/Banner';
import AnimatedNumber from '@/components/ui/AnimatedNumber';
import { api } from '@/lib/api';
import type { PeerReviewItem, PeerReviewStats } from '@/lib/api';
import { usePermissions } from '@/lib/permissions';
import { reportHref } from '@/lib/routes';
import {
  PEER_REVIEW_CATEGORIES,
  PEER_REVIEW_COMPLEXITY,
  PEER_REVIEW_SCORES,
  canSubmit,
  categoryRequired,
  formatRate,
  isOpen,
  scoreOption,
  studyLabel,
} from '@/lib/peerReview';

export default function PeerReviewPage() {
  const { can, loading: permsLoading } = usePermissions();
  const isProgrammeAdmin = can('peer_review.manage');
  const canScore = can('peer_review.submit');

  const [queue, setQueue] = useState<PeerReviewItem[] | null>(null);
  const [feedback, setFeedback] = useState<PeerReviewItem[]>([]);
  const [stats, setStats] = useState<PeerReviewStats | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [statsError, setStatsError] = useState<string | null>(null);

  const [activeId, setActiveId] = useState<string | null>(null);
  const [score, setScore] = useState<number | null>(null);
  const [category, setCategory] = useState<number | null>(null);
  const [complexity, setComplexity] = useState<number>(0);
  const [comments, setComments] = useState('');
  const [saving, setSaving] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const [flash, setFlash] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoadError(null);
    try {
      const [mine, mineAsAuthor] = await Promise.all([
        api.peerReview.mine(),
        api.peerReview.mine({ as: 'author' }).catch(() => [] as PeerReviewItem[]),
      ]);
      setQueue(mine);
      setFeedback(mineAsAuthor);
    } catch (e) {
      setQueue(null);
      setLoadError(e instanceof Error ? e.message : 'Could not load your peer reviews.');
    }
  }, []);

  const loadStats = useCallback(async () => {
    setStatsError(null);
    try {
      setStats(await api.peerReview.stats());
    } catch (e) {
      setStats(null);
      setStatsError(e instanceof Error ? e.message : 'Could not load the quality dashboard.');
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    if (isProgrammeAdmin) void loadStats();
  }, [isProgrammeAdmin, loadStats]);

  const open = useMemo(() => (queue ?? []).filter(isOpen), [queue]);
  const done = useMemo(() => (queue ?? []).filter((r) => !isOpen(r)), [queue]);
  const active = useMemo(
    () => (activeId ? (queue ?? []).find((r) => r.id === activeId) ?? null : null),
    [activeId, queue],
  );

  function resetForm() {
    setScore(null);
    setCategory(null);
    setComplexity(0);
    setComments('');
    setFormError(null);
  }

  async function openReview(review: PeerReviewItem) {
    resetForm();
    setActiveId(review.id);
    setFlash(null);
    if (review.status === 'Assigned') {
      try {
        const started = await api.peerReview.start(review.id);
        setQueue((prev) => (prev ?? []).map((r) => (r.id === started.id ? started : r)));
      } catch {
        // Marking it opened is a convenience, not a precondition for scoring.
      }
    }
  }

  function pickScore(next: number) {
    setScore(next);
    // A concurring review cannot carry a discrepancy category — clear any stale one
    // rather than letting the server reject the submission.
    if (!categoryRequired(next)) setCategory(null);
    setFormError(null);
  }

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!active || !canSubmit(score, category)) return;
    setSaving(true);
    setFormError(null);
    try {
      const saved = await api.peerReview.submit(active.id, {
        score: score as 1 | 2 | 3 | 4,
        discrepancyCategory: category ?? 0,
        complexity,
        comments: comments.trim(),
      });
      setQueue((prev) => (prev ?? []).map((r) => (r.id === saved.id ? saved : r)));
      setActiveId(null);
      resetForm();
      setFlash('Review submitted. Thanks — it now counts toward the quality benchmark.');
      if (isProgrammeAdmin) void loadStats();
    } catch (err) {
      setFormError(err instanceof Error ? err.message : 'Could not submit the review.');
    } finally {
      setSaving(false);
    }
  }

  return (
    <Container>
      <PageHeader
        title="Peer review"
        description="Second reads of signed reports, scored on the RADPEER scale. A quality benchmark — it never changes or re-signs the report."
        secondaryActions={
          <Link
            href="/quality"
            className="ghost"
            style={{ textDecoration: 'none', display: 'inline-flex', alignItems: 'center', gap: 6 }}
          >
            Quality overview
            <ArrowRight size={15} strokeWidth={1.8} aria-hidden />
          </Link>
        }
      />

      {flash && (
        <Banner tone="info" title="Review recorded">
          {flash}
        </Banner>
      )}

      {/* ── My review queue (PR-007) ─────────────────────────────────── */}
      <div className="rp-panel rp-anim-fade-in-up" aria-live="polite" aria-busy={queue === null && !loadError}>
        <div className="rp-panel-title">
          <UsersRound size={15} strokeWidth={1.8} aria-hidden style={{ verticalAlign: -2, marginRight: 6 }} />
          Cases to review
          {open.length > 0 && <span className="badge info" style={{ marginLeft: 8 }}>{open.length}</span>}
        </div>

        {loadError ? (
          <ErrorState message={loadError} onRetry={load} />
        ) : queue === null ? (
          <TableSkeleton rows={4} cols={4} />
        ) : open.length === 0 ? (
          <EmptyState
            title="Nothing waiting for you"
            description="When a colleague's signed report is assigned to you for a second read, it will appear here."
          />
        ) : (
          <table className="rp-table">
            <thead>
              <tr>
                <th>Study</th>
                <th>Reason</th>
                <th>Original reader</th>
                <th>Assigned</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {open.map((review) => (
                <tr key={review.id}>
                  <td>{studyLabel(review)}</td>
                  <td>
                    <span className="badge">{review.reviewType}</span>
                  </td>
                  <td>
                    {review.authorHidden ? (
                      <span className="badge" title="You are reviewing this case blind — the reading radiologist is revealed once you submit your score.">
                        <EyeOff size={12} strokeWidth={1.8} aria-hidden style={{ verticalAlign: -2, marginRight: 4 }} />
                        Hidden until you score
                      </span>
                    ) : (
                      review.originalAuthorName ?? '—'
                    )}
                  </td>
                  <td>{new Date(review.createdAt).toLocaleDateString()}</td>
                  <td style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
                    <Link href={reportHref(review.reportId)} className="subtle" style={{ textDecoration: 'none' }}>
                      Open report
                    </Link>
                    <button
                      type="button"
                      className="primary-ghost"
                      onClick={() => void openReview(review)}
                      disabled={!canScore}
                      title={canScore ? undefined : 'Your role can read peer reviews but not score them.'}
                    >
                      Review
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {/* ── Scoring form (PR-003) ────────────────────────────────────── */}
      {active && (
        <div className="rp-panel rp-anim-fade-in-up">
          <div className="rp-panel-title">Score this read — {studyLabel(active)}</div>

          {active.authorHidden && (
            <Banner tone="info" title="Blinded review">
              You are scoring the interpretation, not the colleague. The reading radiologist&apos;s
              name is revealed to you once you submit.
            </Banner>
          )}

          <form onSubmit={submit}>
            <fieldset
              className="section-block"
              style={{ border: 0, padding: 0, margin: '0 0 14px' }}
            >
              <legend
                style={{
                  fontSize: 11,
                  textTransform: 'uppercase',
                  letterSpacing: '0.06em',
                  color: 'var(--text-muted)',
                  fontWeight: 600,
                  marginBottom: 6,
                }}
              >
                How does your read compare?
              </legend>
              <div style={{ display: 'grid', gap: 8 }}>
                {PEER_REVIEW_SCORES.map((option) => (
                  <label
                    key={option.value}
                    htmlFor={`pr-score-${option.value}`}
                    className="rp-stat-tile"
                    style={{
                      display: 'flex',
                      gap: 10,
                      alignItems: 'flex-start',
                      cursor: 'pointer',
                      background: score === option.value ? 'var(--bg-selected)' : undefined,
                    }}
                  >
                    <input
                      id={`pr-score-${option.value}`}
                      type="radio"
                      name="pr-score"
                      value={option.value}
                      checked={score === option.value}
                      onChange={() => pickScore(option.value)}
                      style={{ marginTop: 3 }}
                    />
                    <span>
                      <span style={{ fontWeight: 600 }}>{option.label}</span>{' '}
                      <span className={`badge ${option.tone}`}>RADPEER {option.value}</span>
                      <span className="rp-page-sub" style={{ display: 'block', marginTop: 2 }}>
                        {option.help}
                      </span>
                    </span>
                  </label>
                ))}
              </div>
            </fieldset>

            {categoryRequired(score) && (
              <div className="section-block">
                <label htmlFor="pr-category">What kind of discrepancy?</label>
                <select
                  id="pr-category"
                  value={category ?? ''}
                  onChange={(e) => {
                    setCategory(e.target.value ? Number(e.target.value) : null);
                    setFormError(null);
                  }}
                >
                  <option value="">Choose one…</option>
                  {PEER_REVIEW_CATEGORIES.map((c) => (
                    <option key={c.value} value={c.value}>
                      {c.label}
                    </option>
                  ))}
                </select>
              </div>
            )}

            <div className="section-block">
              <label htmlFor="pr-complexity">How difficult was this study?</label>
              <select
                id="pr-complexity"
                value={complexity}
                onChange={(e) => setComplexity(Number(e.target.value))}
              >
                {PEER_REVIEW_COMPLEXITY.map((c) => (
                  <option key={c.value} value={c.value}>
                    {c.label}
                  </option>
                ))}
              </select>
            </div>

            <div className="section-block">
              <label htmlFor="pr-comments">Comments</label>
              <textarea
                id="pr-comments"
                value={comments}
                onChange={(e) => setComments(e.target.value)}
                placeholder="What did you see differently, and why does it matter?"
              />
            </div>

            {formError && (
              <Banner tone="danger" title="Could not submit">
                {formError}
              </Banner>
            )}

            <div style={{ display: 'flex', gap: 8 }}>
              <button type="submit" className="primary" disabled={saving || !canSubmit(score, category)}>
                {saving ? 'Submitting…' : 'Submit review'}
              </button>
              <button
                type="button"
                className="ghost"
                onClick={() => {
                  setActiveId(null);
                  resetForm();
                }}
                disabled={saving}
              >
                Cancel
              </button>
            </div>
          </form>
        </div>
      )}

      {/* ── My completed reviews ─────────────────────────────────────── */}
      {done.length > 0 && (
        <div className="rp-panel rp-anim-fade-in-up">
          <div className="rp-panel-title">Reviews you have completed</div>
          <table className="rp-table">
            <thead>
              <tr>
                <th>Study</th>
                <th>Original reader</th>
                <th>Your score</th>
                <th>Completed</th>
              </tr>
            </thead>
            <tbody>
              {done.map((review) => {
                const opt = scoreOption(review.score);
                return (
                  <tr key={review.id}>
                    <td>{studyLabel(review)}</td>
                    <td>{review.originalAuthorName ?? '—'}</td>
                    <td>
                      <span className={`badge ${opt?.tone ?? 'info'}`}>
                        {opt ? opt.label : review.scoreName}
                      </span>
                    </td>
                    <td>{review.completedAt ? new Date(review.completedAt).toLocaleDateString() : '—'}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      {/* ── Feedback on my own reports (PR-008) ──────────────────────── */}
      {feedback.length > 0 && (
        <div className="rp-panel rp-anim-fade-in-up">
          <div className="rp-panel-title">
            <ShieldQuestion size={15} strokeWidth={1.8} aria-hidden style={{ verticalAlign: -2, marginRight: 6 }} />
            Feedback on your reports
          </div>
          <p className="rp-page-sub" style={{ marginBottom: 12 }}>
            What second readers recorded about studies you signed. If you believe a score is wrong,
            raise it with your medical director — nothing here changes the report.
          </p>
          <table className="rp-table">
            <thead>
              <tr>
                <th>Study</th>
                <th>Reviewer</th>
                <th>Score</th>
                <th>Comments</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              {feedback.map((review) => {
                const opt = scoreOption(review.score);
                return (
                  <tr key={review.id}>
                    <td>{studyLabel(review)}</td>
                    <td>{review.reviewerName ?? '—'}</td>
                    <td>
                      <span className={`badge ${opt?.tone ?? 'info'}`}>
                        {opt ? opt.label : review.scoreName}
                      </span>
                    </td>
                    <td>{review.comments || '—'}</td>
                    <td>
                      <span className={`badge ${review.status === 'Disputed' ? 'warn' : 'ok'}`}>
                        {review.status}
                      </span>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      {/* ── Concordance dashboard (PR-005 / PR-009) ──────────────────── */}
      {!permsLoading && isProgrammeAdmin && (
        <div className="rp-panel rp-anim-fade-in-up" aria-live="polite" aria-busy={stats === null && !statsError}>
          <div className="rp-panel-title">Concordance &amp; discrepancies</div>
          <p className="rp-page-sub" style={{ marginBottom: 12 }}>
            Last 90 days. Concordance is the share of reviews scored &ldquo;I agree&rdquo; — a
            benchmark to understand, not a number to drive to 100%.
          </p>

          {statsError ? (
            <ErrorState message={statsError} onRetry={loadStats} />
          ) : stats === null ? (
            <TableSkeleton rows={4} cols={4} />
          ) : stats.totals.reviewed === 0 ? (
            <EmptyState
              title="No completed reviews in this window"
              description="Once reviewers submit scores, per-radiologist concordance and discrepancy patterns appear here."
            />
          ) : (
            <>
              <div className="metric-grid rp-stagger">
                <div className="metric-card" data-tone="info">
                  <div className="metric-card-value">
                    <AnimatedNumber value={stats.totals.reviewed} />
                  </div>
                  <div className="metric-card-label">Reviews completed</div>
                </div>
                <div className="metric-card" data-tone="ready">
                  <div className="metric-card-value">{formatRate(stats.totals.concordanceRate)}</div>
                  <div className="metric-card-label">Concordance rate</div>
                </div>
                <div className="metric-card" data-tone={stats.totals.discrepancies > 0 ? 'review' : 'ready'}>
                  <div className="metric-card-value">
                    <AnimatedNumber value={stats.totals.discrepancies} />
                  </div>
                  <div className="metric-card-label">Discrepancies recorded</div>
                </div>
                <div className="metric-card" data-tone="info">
                  <div className="metric-card-value">
                    <AnimatedNumber value={stats.totals.pending} />
                  </div>
                  <div className="metric-card-label">Awaiting review</div>
                </div>
              </div>

              <table className="rp-table" style={{ marginTop: 16 }}>
                <thead>
                  <tr>
                    <th>Radiologist</th>
                    <th>Reviewed</th>
                    <th>Concordance</th>
                    <th>Perceptual</th>
                    <th>Interpretive</th>
                    <th>Communication</th>
                    <th>Technique</th>
                    <th>Disputed</th>
                  </tr>
                </thead>
                <tbody>
                  {stats.perReader.map((row) => (
                    <tr key={row.userId}>
                      <td>{row.displayName}</td>
                      <td>{row.reviewed}</td>
                      <td>
                        <span
                          className={`badge ${
                            row.concordanceRate >= 0.9 ? 'ok' : row.concordanceRate >= 0.75 ? 'warn' : 'danger'
                          }`}
                        >
                          {formatRate(row.concordanceRate)}
                        </span>
                      </td>
                      <td>{row.byCategory.perceptual}</td>
                      <td>{row.byCategory.interpretive}</td>
                      <td>{row.byCategory.communication}</td>
                      <td>{row.byCategory.technique}</td>
                      <td>{row.disputed}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </>
          )}
        </div>
      )}
    </Container>
  );
}
