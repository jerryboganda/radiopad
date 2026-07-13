'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { Plus, ArrowRight } from 'lucide-react';
import { api, type Report } from '@/lib/api';
import { reportHref } from '@/lib/routes';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import { TableSkeleton } from '@/components/ui/Skeleton';
import EmptyState from '@/components/ui/EmptyState';
import ErrorState from '@/components/ui/ErrorState';
import StatusBadge, { reportStatusTone } from '@/components/ui/StatusBadge';
import AnimatedNumber from '@/components/ui/AnimatedNumber';

/**
 * Dashboard — the landing page of the desktop app. One glance answers:
 * how much work is there, what's waiting on me, and what happened recently.
 */

type AuditEvent = {
  id: string;
  action: number | string;
  detailsJson: string;
  createdAt: string;
};

const ACTION_LABEL: Record<number, string> = {
  0: 'AI request',
  1: 'AI response',
  2: 'Report edited',
  3: 'Report exported',
  4: 'Report acknowledged',
  5: 'Provider blocked',
  6: 'Rulebook approved',
  7: 'Rulebook deprecated',
  8: 'User login',
  9: 'Policy violation',
  54: 'Provider configured',
};

export default function DashboardPage() {
  const router = useRouter();

  // ── Reports (feeds both the KPI row and the case queue) ──────────
  const [reports, setReports] = useState<Report[] | null>(null);
  const [reportsError, setReportsError] = useState<string | null>(null);
  const [reportsLoading, setReportsLoading] = useState(true);

  const loadReports = useCallback(() => {
    setReportsLoading(true);
    setReportsError(null);
    api.reports
      .list()
      .then(setReports)
      .catch((e: Error) => setReportsError(e.message || 'Failed to load reports'))
      .finally(() => setReportsLoading(false));
  }, []);

  // ── Recent activity ───────────────────────────────────────────────
  const [events, setEvents] = useState<AuditEvent[] | null>(null);
  const [eventsError, setEventsError] = useState<string | null>(null);
  const [eventsLoading, setEventsLoading] = useState(true);

  const loadEvents = useCallback(() => {
    setEventsLoading(true);
    setEventsError(null);
    api.audit
      .query({ take: 8 })
      .then((rows) => setEvents(rows as AuditEvent[]))
      .catch((e: Error) => setEventsError(e.message || 'Failed to load activity'))
      .finally(() => setEventsLoading(false));
  }, []);

  useEffect(() => {
    loadReports();
    loadEvents();
  }, [loadReports, loadEvents]);

  const counts = countByStatus(reports ?? []);
  const queue = (reports ?? [])
    .filter((r) => !isFinal(r.status))
    .sort((a, b) => Date.parse(b.updatedAt) - Date.parse(a.updatedAt))
    .slice(0, 6);

  return (
    <Container>
      <PageHeader
        title="Dashboard"
        description="Your reporting workspace at a glance — open cases, progress, and recent activity."
        primaryAction={
          <Link href="/reports/new" className="primary" style={{ textDecoration: 'none', display: 'inline-flex', alignItems: 'center', gap: 6 }}>
            <Plus size={15} strokeWidth={2} aria-hidden />
            New report
          </Link>
        }
        secondaryActions={
          <Link href="/reports" className="ghost" style={{ textDecoration: 'none', display: 'inline-flex', alignItems: 'center', gap: 6 }}>
            Open worklist
            <ArrowRight size={15} strokeWidth={1.8} aria-hidden />
          </Link>
        }
      />

      {/* ── KPI row ──────────────────────────────────────────────── */}
      <section aria-label="Report totals" aria-live="polite" aria-busy={reportsLoading} style={{ marginBottom: 24 }}>
        {reportsLoading && !reports ? (
          <div className="metric-grid">
            {['Total reports', 'Drafts', 'Validated', 'Exported'].map((label) => (
              <div key={label} className="metric-card">
                <div className="metric-card-value text-ink-soft">—</div>
                <div className="metric-card-label">{label}</div>
              </div>
            ))}
          </div>
        ) : reportsError && !reports ? (
          <ErrorState title="Couldn't load report totals" message={reportsError} onRetry={loadReports} />
        ) : (
          <div className="metric-grid rp-stagger">
            <div className="metric-card" data-tone="info">
              <div className="metric-card-value"><AnimatedNumber value={counts.total} /></div>
              <div className="metric-card-label">Total reports</div>
            </div>
            <div className="metric-card" data-tone="review">
              <div className="metric-card-value"><AnimatedNumber value={counts.draft} /></div>
              <div className="metric-card-label">Drafts in progress</div>
            </div>
            <div className="metric-card" data-tone="info">
              <div className="metric-card-value"><AnimatedNumber value={counts.validated} /></div>
              <div className="metric-card-label">Validated</div>
            </div>
            <div className="metric-card" data-tone="ready">
              <div className="metric-card-value"><AnimatedNumber value={counts.exported} /></div>
              <div className="metric-card-label">Exported</div>
            </div>
          </div>
        )}
      </section>

      <div className="rp-grid-2" style={{ alignItems: 'start' }}>
        {/* ── Case queue ─────────────────────────────────────────── */}
        <section className="rp-panel rp-anim-fade-in-up" aria-live="polite" aria-busy={reportsLoading}>
          <div className="rp-panel-title">Case queue</div>
          {reportsLoading && !reports ? (
            <TableSkeleton rows={6} cols={4} />
          ) : reportsError && !reports ? (
            <ErrorState title="Couldn't load the case queue" message={reportsError} onRetry={loadReports} />
          ) : queue.length === 0 ? (
            <EmptyState
              title="Queue is clear"
              description="No open cases right now. New and in-progress reports will show up here."
              action={
                <Link href="/reports/new" className="primary-ghost" style={{ textDecoration: 'none' }}>
                  Start a report
                </Link>
              }
            />
          ) : (
            <div className="table-wrap">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Accession</th>
                    <th>Study</th>
                    <th>Status</th>
                    <th>Waiting</th>
                  </tr>
                </thead>
                <tbody className="rp-stagger">
                  {queue.map((r) => (
                    <tr
                      key={r.id}
                      onClick={() => router.push(reportHref(r.id))}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter' || e.key === ' ') {
                          e.preventDefault();
                          router.push(reportHref(r.id));
                        }
                      }}
                      tabIndex={0}
                      role="link"
                      aria-label={`Open report ${r.study.accessionNumber || r.id}`}
                      style={{ cursor: 'pointer' }}
                    >
                      <td>{r.study.accessionNumber || '—'}</td>
                      <td>
                        {r.study.modality || '—'}
                        {r.study.bodyPart ? <span className="text-ink-soft"> · {r.study.bodyPart}</span> : null}
                      </td>
                      <td>
                        <StatusBadge tone={reportStatusTone(r.status)}>{statusLabel(r.status)}</StatusBadge>
                      </td>
                      <td className="text-ink-soft" style={{ whiteSpace: 'nowrap' }}>{waitTime(r.updatedAt)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          {queue.length > 0 && (
            <p className="rp-page-sub" style={{ marginTop: 12, marginBottom: 0 }}>
              Showing the {queue.length === 1 ? 'most recent open case' : `${queue.length} most recent open cases`}.{' '}
              <Link href="/reports">See the full worklist</Link>
            </p>
          )}
        </section>

        {/* ── Recent activity ────────────────────────────────────── */}
        <section className="rp-panel rp-anim-fade-in-up" aria-live="polite" aria-busy={eventsLoading}>
          <div className="rp-panel-title">Recent activity</div>
          {eventsLoading && !events ? (
            <TableSkeleton rows={6} cols={2} />
          ) : eventsError && !events ? (
            <ErrorState title="Couldn't load activity" message={eventsError} onRetry={loadEvents} />
          ) : !events || events.length === 0 ? (
            <EmptyState
              title="No activity yet"
              description="Actions across your workspace — edits, exports, AI requests — will appear here as they happen."
            />
          ) : (
            <div className="table-wrap">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>When</th>
                    <th>What happened</th>
                  </tr>
                </thead>
                <tbody className="rp-stagger">
                  {events.map((e) => (
                    <tr key={e.id}>
                      <td className="text-ink-soft" style={{ whiteSpace: 'nowrap' }}>{waitTime(e.createdAt)} ago</td>
                      <td>
                        <span className={`badge ${badgeFor(e.action)}`}>{actionLabel(e.action)}</span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          {events && events.length > 0 && (
            <p className="rp-page-sub" style={{ marginTop: 12, marginBottom: 0 }}>
              <Link href="/audit">See the full activity log</Link>
            </p>
          )}
        </section>
      </div>
    </Container>
  );
}

/* ── Helpers ─────────────────────────────────────────────────────────── */

const STATUS_NAMES = ['Draft', 'Validated', 'Acknowledged', 'Exported'] as const;

function statusLabel(s: Report['status']): string {
  if (typeof s === 'string') return s;
  return STATUS_NAMES[s] ?? String(s);
}

function normalizeStatus(s: Report['status']): string {
  return statusLabel(s).toLowerCase();
}

/** Final = signed off and out the door; everything else still needs attention. */
function isFinal(s: Report['status']): boolean {
  const n = normalizeStatus(s);
  return n === 'acknowledged' || n === 'exported';
}

function countByStatus(reports: Report[]) {
  const counts = { total: reports.length, draft: 0, validated: 0, exported: 0 };
  for (const r of reports) {
    const n = normalizeStatus(r.status);
    if (n === 'draft') counts.draft += 1;
    else if (n === 'validated') counts.validated += 1;
    else if (n === 'exported') counts.exported += 1;
  }
  return counts;
}

/** Compact "how long has this been sitting" formatter — 45s / 12m / 3h / 2d. */
function waitTime(iso: string): string {
  const ms = Date.now() - Date.parse(iso);
  if (!Number.isFinite(ms) || ms < 0) return '—';
  const s = Math.floor(ms / 1000);
  if (s < 60) return `${s}s`;
  const m = Math.floor(s / 60);
  if (m < 60) return `${m}m`;
  const h = Math.floor(m / 60);
  if (h < 24) return `${h}h`;
  const d = Math.floor(h / 24);
  return `${d}d`;
}

function actionLabel(a: AuditEvent['action']): string {
  if (typeof a === 'string') return a;
  return ACTION_LABEL[a] ?? `Action ${a}`;
}

function badgeFor(a: AuditEvent['action']): string {
  const n = typeof a === 'number' ? a : -1;
  if (n === 9 || n === 5) return 'danger';
  if (n === 4 || n === 6) return 'ok';
  if (n === 0 || n === 1) return 'ai';
  return 'info';
}
