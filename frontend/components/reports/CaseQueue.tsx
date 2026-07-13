'use client';

// RC-01 case queue card — the other reports on this tenant's worklist, shown
// beside the study context so the radiologist can hop between cases without
// leaving the composer. Data comes from the existing `api.reports.list()`;
// no priority field exists on reports, so the status chip carries the row
// state (honest data only — nothing fabricated).
import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { ListTodo } from 'lucide-react';
import { api, type Report } from '@/lib/api';
import Skeleton from '@/components/ui/Skeleton';
import ErrorState from '@/components/ui/ErrorState';
import EmptyState from '@/components/ui/EmptyState';

const MAX_ROWS = 6;

/** Compact relative time — "just now", "4 min", "2 h", "3 d". */
export function timeAgo(iso: string): string {
  try {
    const ms = Date.now() - new Date(iso).getTime();
    if (!Number.isFinite(ms) || ms < 0) return '';
    const min = Math.floor(ms / 60_000);
    if (min < 1) return 'just now';
    if (min < 60) return `${min} min`;
    const h = Math.floor(min / 60);
    if (h < 48) return `${h} h`;
    return `${Math.floor(h / 24)} d`;
  } catch {
    return '';
  }
}

function statusName(s: Report['status']): string {
  if (typeof s === 'string') return s;
  return ['Draft', 'Validated', 'Acknowledged', 'Exported'][s] ?? String(s);
}

function statusChipTone(s: Report['status']): string {
  const label = statusName(s);
  if (label === 'Acknowledged' || label === 'Exported') return 'ok';
  if (label === 'Validated') return 'info';
  return '';
}

export default function CaseQueue({ currentId }: { currentId: string }) {
  const router = useRouter();
  const [items, setItems] = useState<Report[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let cancelled = false;
    setError(null);
    api.reports
      .list()
      .then((rows) => {
        if (cancelled) return;
        // Current report first, then most recently touched.
        const sorted = [...rows].sort((a, b) => {
          if (a.id === currentId) return -1;
          if (b.id === currentId) return 1;
          return (b.updatedAt || '').localeCompare(a.updatedAt || '');
        });
        setItems(sorted);
      })
      .catch((e: Error) => {
        if (!cancelled) setError(e.message);
      });
    return () => {
      cancelled = true;
    };
  }, [currentId, reloadKey]);

  const total = items?.length ?? 0;

  return (
    <div className="rp-queue">
      <div className="rp-queue-head">
        <span className="rp-queue-title">
          <ListTodo size={13} aria-hidden /> Case queue{items ? ` (${total})` : ''}
        </span>
      </div>

      {error ? (
        <ErrorState title="Queue unavailable" message={error} onRetry={() => setReloadKey((k) => k + 1)} />
      ) : items === null ? (
        <div role="status" aria-busy="true" style={{ padding: '4px 0' }}>
          <Skeleton variant="row" />
          <Skeleton variant="row" />
          <Skeleton variant="row" />
          <span className="rp-sr-only">Loading case queue…</span>
        </div>
      ) : total === 0 ? (
        <EmptyState title="No reports in the queue" />
      ) : (
        <>
          <table className="rp-queue-table">
            <thead>
              <tr>
                <th>Report</th>
                <th>Procedure</th>
                <th>Updated</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              {items.slice(0, MAX_ROWS).map((r) => {
                const isCurrent = r.id === currentId;
                return (
                  <tr
                    key={r.id}
                    className={isCurrent ? 'is-current' : ''}
                    tabIndex={0}
                    role="link"
                    aria-current={isCurrent ? 'true' : undefined}
                    onClick={() => {
                      if (!isCurrent) router.push(`/reports/view?id=${r.id}`);
                    }}
                    onKeyDown={(e) => {
                      if ((e.key === 'Enter' || e.key === ' ') && !isCurrent) {
                        e.preventDefault();
                        router.push(`/reports/view?id=${r.id}`);
                      }
                    }}
                  >
                    <td>
                      <code className="rp-queue-id">{r.study.accessionNumber || r.id.slice(0, 8)}</code>
                    </td>
                    <td className="rp-queue-proc">
                      {[r.study.modality, r.study.bodyPart].filter(Boolean).join(' ') || '—'}
                    </td>
                    <td className="rp-queue-wait">{timeAgo(r.updatedAt)}</td>
                    <td>
                      <span className={`badge ${statusChipTone(r.status)}`}>
                        {isCurrent ? 'In progress' : statusName(r.status)}
                      </span>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
          <a className="rp-queue-more" href="/reports">
            View full queue ({total})
          </a>
        </>
      )}
    </div>
  );
}
