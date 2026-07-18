'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import { RefreshCw } from 'lucide-react';
import { api, type Report } from '@/lib/api';
import { reportHref } from '@/lib/routes';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import { TableSkeleton } from '@/components/ui/Skeleton';
import EmptyState from '@/components/ui/EmptyState';
import ErrorState from '@/components/ui/ErrorState';
import Banner from '@/components/ui/Banner';
import PermissionGate from '@/components/ui/PermissionGate';

/** RC-01 — Worklist. The Report Composer's case queue as a full page: every
 * report in the workspace, ordered by priority and how long it has waited. */

const REFRESH_MS = 30_000;
/** A draft edited within this window counts as actively being worked on. */
const IN_PROGRESS_WINDOW_MS = 15 * 60 * 1000;

type Priority = 'STAT' | 'Urgent' | 'Routine';
type QueueStatus = 'In progress' | 'Waiting' | 'Validated' | 'Acknowledged' | 'Exported';

const STATUS_OPTIONS: Array<{ value: string; label: string }> = [
  { value: 'all', label: 'All statuses' },
  { value: 'In progress', label: 'In progress' },
  { value: 'Waiting', label: 'Waiting' },
  { value: 'Validated', label: 'Validated' },
  { value: 'Acknowledged', label: 'Acknowledged' },
  { value: 'Exported', label: 'Exported' },
];

function statusName(s: Report['status']): string {
  if (typeof s === 'string') return s;
  return ['Draft', 'Validated', 'Acknowledged', 'Exported'][s] ?? String(s);
}

/** Prefer the authoritative backend priority (RIS-fed or PATCH-set) when it is a real
 * escalation; a default Routine falls back to reading the clinical indication honestly:
 * a whole-word "stat" means STAT, urgent/emergency language means Urgent. Word boundaries
 * keep "status", "prostate" or "metastatic" from triggering STAT. */
function derivePriority(r: Report): Priority {
  const p = (r as { priority?: number | string }).priority;
  const name =
    p === undefined || p === null ? null
    : typeof p === 'string' ? p
    : (['Routine', 'Urgent', 'Stat'][p] ?? null);
  if (name === 'Stat') return 'STAT';
  if (name === 'Urgent') return 'Urgent';

  const text = r.indication || '';
  if (/\bstat\b/i.test(text)) return 'STAT';
  if (/\b(urgent(ly)?|emergen\w*|immediate\w*|asap)\b/i.test(text)) return 'Urgent';
  return 'Routine';
}

/** Drafts touched recently are being read right now; older drafts wait in the
 * queue. Signed-off stages keep their own names. */
function deriveQueueStatus(r: Report, now: number): QueueStatus {
  const name = statusName(r.status);
  if (name === 'Draft') {
    const updated = new Date(r.updatedAt).getTime();
    return now - updated <= IN_PROGRESS_WINDOW_MS ? 'In progress' : 'Waiting';
  }
  if (name === 'Validated' || name === 'Acknowledged' || name === 'Exported') return name;
  return 'Waiting';
}

/** mm:ss under an hour, then "Xh Ym". The Report type carries no creation
 * timestamp, so the wait clock runs from the last activity (updatedAt). */
function formatWait(sinceMs: number): string {
  const totalSec = Math.max(0, Math.floor(sinceMs / 1000));
  if (totalSec < 3600) {
    const m = Math.floor(totalSec / 60);
    const s = totalSec % 60;
    return `${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
  }
  const h = Math.floor(totalSec / 3600);
  const m = Math.floor((totalSec % 3600) / 60);
  return `${h}h ${m}m`;
}

function procedureLabel(r: Report): string {
  const parts = [r.study.modality, r.study.bodyPart].filter(Boolean);
  const base = parts.join(' ') || 'Unspecified study';
  if (r.study.contrast === 'With') return `${base} w/ Contrast`;
  if (r.study.contrast === 'WithAndWithout') return `${base} w/ + w/o Contrast`;
  if (r.study.contrast === 'None') return `${base} w/o Contrast`;
  return base;
}

function PriorityChip({ priority }: { priority: Priority }) {
  if (priority === 'STAT') {
    return <span className="status-badge" data-tone="stat">STAT</span>;
  }
  if (priority === 'Urgent') {
    return <span className="badge danger">Urgent</span>;
  }
  return <span className="badge ok">Routine</span>;
}

const QUEUE_STATUS_TONE: Record<QueueStatus, string> = {
  'In progress': 'info',
  Waiting: 'muted',
  Validated: 'review',
  Acknowledged: 'ready',
  Exported: 'ready',
};

function QueueStatusChip({ status }: { status: QueueStatus }) {
  return (
    <span className="status-badge" data-tone={QUEUE_STATUS_TONE[status]}>
      {status}
    </span>
  );
}

const PRIORITY_RANK: Record<Priority, number> = { STAT: 0, Urgent: 1, Routine: 2 };

export default function WorklistPage() {
  return (
    <PermissionGate permission="reports.read" title="Worklist">
      <WorklistInner />
    </PermissionGate>
  );
}

function WorklistInner() {
  const router = useRouter();
  const [reports, setReports] = useState<Report[] | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [refreshError, setRefreshError] = useState<string | null>(null);
  const [lastRefreshed, setLastRefreshed] = useState<Date | null>(null);
  const [now, setNow] = useState(() => Date.now());

  const [statusFilter, setStatusFilter] = useState('all');
  const [modalityFilter, setModalityFilter] = useState('all');
  const [query, setQuery] = useState('');

  const hasDataRef = useRef(false);

  const load = useCallback((opts: { silent?: boolean } = {}) => {
    if (!opts.silent) {
      setLoading(true);
      setError(null);
    }
    return api.reports
      .list()
      .then((rows) => {
        hasDataRef.current = true;
        setReports(rows);
        setError(null);
        setRefreshError(null);
        setLastRefreshed(new Date());
        setNow(Date.now());
      })
      .catch((e: Error) => {
        if (hasDataRef.current) {
          // Keep the queue on screen; just flag that it may be stale.
          setRefreshError(e.message || 'Refresh failed');
        } else {
          setError(e.message || 'Failed to load the worklist');
        }
      })
      .finally(() => {
        if (!opts.silent) setLoading(false);
      });
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  // Auto-refresh the queue every 30 seconds without flashing the skeleton.
  useEffect(() => {
    const t = setInterval(() => {
      void load({ silent: true });
    }, REFRESH_MS);
    return () => clearInterval(t);
  }, [load]);

  // Tick the wait-time clocks once a second so mm:ss counters stay live.
  useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(t);
  }, []);

  const modalities = useMemo(() => {
    const set = new Set<string>();
    for (const r of reports ?? []) {
      if (r.study.modality) set.add(r.study.modality);
    }
    return Array.from(set).sort();
  }, [reports]);

  const rows = useMemo(() => {
    const q = query.trim().toLowerCase();
    return (reports ?? [])
      .map((r) => ({
        report: r,
        priority: derivePriority(r),
        queueStatus: deriveQueueStatus(r, now),
        waitMs: now - new Date(r.updatedAt).getTime(),
      }))
      .filter((row) => {
        if (statusFilter !== 'all' && row.queueStatus !== statusFilter) return false;
        if (modalityFilter !== 'all' && row.report.study.modality !== modalityFilter) return false;
        if (q) {
          const hay = [
            row.report.study.accessionNumber,
            row.report.id,
            row.report.study.modality,
            row.report.study.bodyPart,
            row.report.indication,
          ]
            .join(' ')
            .toLowerCase();
          if (!hay.includes(q)) return false;
        }
        return true;
      })
      .sort((a, b) => {
        const p = PRIORITY_RANK[a.priority] - PRIORITY_RANK[b.priority];
        if (p !== 0) return p;
        // Longest-waiting cases first within the same priority.
        return b.waitMs - a.waitMs;
      });
  }, [reports, statusFilter, modalityFilter, query, now]);

  const openReport = useCallback(
    (id: string) => {
      router.push(reportHref(id));
    },
    [router],
  );

  const total = reports?.length ?? 0;
  const filtersActive = statusFilter !== 'all' || modalityFilter !== 'all' || query.trim() !== '';

  return (
    <Container>
      <PageHeader
        title="Worklist"
        description="The live case queue — every report in your workspace, ordered by priority and wait time."
        secondaryActions={
          <button
            type="button"
            className="ghost"
            onClick={() => void load()}
            disabled={loading}
            aria-busy={loading}
            style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}
          >
            <RefreshCw size={15} strokeWidth={1.8} aria-hidden />
            Refresh
          </button>
        }
      />

      {refreshError && (
        <Banner tone="warn" title="Couldn't refresh the queue">
          Showing the last loaded list. {refreshError}
        </Banner>
      )}

      <div className="rp-filter-bar">
        <input
          type="search"
          className="rp-input rp-search"
          placeholder="Search accession, procedure, or indication…"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          aria-label="Search the worklist"
        />
        <select
          className="rp-input"
          value={statusFilter}
          onChange={(e) => setStatusFilter(e.target.value)}
          aria-label="Filter by status"
        >
          {STATUS_OPTIONS.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </select>
        <select
          className="rp-input"
          value={modalityFilter}
          onChange={(e) => setModalityFilter(e.target.value)}
          aria-label="Filter by modality"
        >
          <option value="all">All modalities</option>
          {modalities.map((m) => (
            <option key={m} value={m}>
              {m}
            </option>
          ))}
        </select>
        {reports && (
          <span className="rp-page-sub" style={{ marginLeft: 'auto' }}>
            {rows.length} of {total} case{total === 1 ? '' : 's'}
            {lastRefreshed && ` · updated ${lastRefreshed.toLocaleTimeString()}`}
          </span>
        )}
      </div>

      <section className="rp-panel" aria-live="polite" aria-busy={loading}>
        {loading && !reports ? (
          <TableSkeleton rows={6} cols={5} />
        ) : error && !reports ? (
          <ErrorState
            title="Couldn't load the worklist"
            message={error}
            onRetry={() => void load()}
          />
        ) : total === 0 ? (
          <EmptyState
            title="The queue is clear"
            description="New cases will appear here as reports are created. This list refreshes automatically."
          />
        ) : rows.length === 0 ? (
          <EmptyState
            title="No matching cases"
            description={
              filtersActive
                ? 'Nothing in the queue matches these filters. Try widening the status or modality, or clearing the search.'
                : 'Nothing in the queue right now.'
            }
          />
        ) : (
          <table className="rp-table">
            <thead>
              <tr>
                <th>Patient / Report</th>
                <th>Procedure</th>
                <th>Priority</th>
                <th>Wait time</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody className="rp-stagger">
              {rows.map(({ report: r, priority, queueStatus, waitMs }) => (
                <tr
                  key={r.id}
                  tabIndex={0}
                  onClick={() => openReport(r.id)}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter' || e.key === ' ') {
                      e.preventDefault();
                      openReport(r.id);
                    }
                  }}
                  style={{ cursor: 'pointer' }}
                  aria-label={`Open report ${r.study.accessionNumber || r.id}`}
                >
                  <td>
                    <div style={{ fontWeight: 600 }}>{r.study.accessionNumber || 'No accession'}</div>
                    <div className="rp-page-sub">
                      <code>{r.id.slice(0, 8)}</code>
                    </div>
                  </td>
                  <td>{procedureLabel(r)}</td>
                  <td>
                    <PriorityChip priority={priority} />
                  </td>
                  <td title="Measured from the report's last activity">{formatWait(waitMs)}</td>
                  <td>
                    <QueueStatusChip status={queueStatus} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
    </Container>
  );
}
