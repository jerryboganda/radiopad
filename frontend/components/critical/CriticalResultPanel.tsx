'use client';

/**
 * PRD §14.15 (CR-001..010) — critical-results panel for the report editor.
 *
 * Shows every critical result already logged against this report, lets the
 * radiologist log a new one (criticality picker + finding summary), record the
 * communication to the ordering clinician, and capture the read-back
 * acknowledgement. A due-in countdown makes the deadline derived from the
 * criticality class visible while there is still time to act on it.
 *
 * Safety: nothing here communicates or acknowledges automatically — every
 * transition is an explicit click, and the server re-checks the permission and
 * appends the audit row. Criticality badges follow the documented severity map
 * (Red→red, Orange→amber, Yellow→blue) and are never hue-only: each badge
 * carries its label text.
 */

import { useCallback, useEffect, useState } from 'react';
import { api } from '@/lib/api';
import {
  CRITICALITY_BADGE_TONE,
  CRITICALITY_LABELS,
  CRITICAL_STATUS_BADGE_TONE,
  COMMUNICATION_METHOD_LABELS,
} from '@/lib/api';
import type {
  CriticalResult,
  CriticalityLevel,
  CriticalCommunicationMethod,
} from '@/lib/api';
import { usePermissions } from '@/lib/permissions';
import { TableSkeleton } from '@/components/ui/Skeleton';
import EmptyState from '@/components/ui/EmptyState';
import ErrorState from '@/components/ui/ErrorState';
import { X } from 'lucide-react';

/**
 * Per-report dismissal of this panel. Only ~10% of reports ever get a critical
 * result, so on the other 90% the panel is pure vertical noise above the
 * section cards — hiding it is a real decluttering win.
 *
 * Stored per report id, so dismissing it on one report never hides it on
 * another. Reads are guarded because localStorage throws in private-mode
 * Safari and is absent during SSR.
 */
const DISMISS_KEY_PREFIX = 'radiopad.criticalPanel.dismissed.';

function readDismissed(reportId: string): boolean {
  if (typeof window === 'undefined') return false;
  try {
    return window.localStorage.getItem(DISMISS_KEY_PREFIX + reportId) === '1';
  } catch {
    return false;
  }
}

function writeDismissed(reportId: string, value: boolean): void {
  if (typeof window === 'undefined') return;
  try {
    if (value) window.localStorage.setItem(DISMISS_KEY_PREFIX + reportId, '1');
    else window.localStorage.removeItem(DISMISS_KEY_PREFIX + reportId);
  } catch {
    /* dismissal is a convenience — never break the panel over storage */
  }
}

const CRITICALITY_OPTIONS: CriticalityLevel[] = ['Red', 'Orange', 'Yellow'];
const METHOD_OPTIONS: CriticalCommunicationMethod[] = [
  'Phone',
  'SecureMessage',
  'InPerson',
  'Other',
];

/**
 * "due in 12 min" / "overdue by 5 min". Returns null once the loop is closed —
 * a countdown on an acknowledged result is noise, not information.
 */
export function formatDueIn(dueAt: string, now: number): string {
  const deltaMs = new Date(dueAt).getTime() - now;
  const mins = Math.round(Math.abs(deltaMs) / 60000);
  const unit = mins >= 120 ? `${Math.round(mins / 60)} h` : `${mins} min`;
  return deltaMs >= 0 ? `due in ${unit}` : `overdue by ${unit}`;
}

function isOpenLoop(c: CriticalResult): boolean {
  return c.status !== 'Acknowledged' && c.status !== 'Closed';
}

export interface CriticalResultPanelProps {
  reportId: string;
}

export default function CriticalResultPanel({ reportId }: CriticalResultPanelProps) {
  const { can } = usePermissions();
  const canManage = can('critical_results.manage');

  const [items, setItems] = useState<CriticalResult[]>([]);
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [actionErr, setActionErr] = useState<string | null>(null);

  // Create form.
  const [criticality, setCriticality] = useState<CriticalityLevel>('Red');
  const [summary, setSummary] = useState('');
  const [creating, setCreating] = useState(false);

  // Inline communicate form — holds the id of the result being communicated.
  const [communicatingId, setCommunicatingId] = useState<string | null>(null);
  const [communicatedTo, setCommunicatedTo] = useState('');
  const [method, setMethod] = useState<CriticalCommunicationMethod>('Phone');

  // Read on mount rather than in the initial useState value: localStorage is
  // unavailable during SSR, and seeding state from it directly would make the
  // server and first client render disagree (hydration mismatch).
  const [dismissed, setDismissed] = useState(false);
  useEffect(() => {
    setDismissed(readDismissed(reportId));
  }, [reportId]);

  // Keep the countdown honest without re-fetching.
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), 30000);
    return () => clearInterval(t);
  }, []);

  const load = useCallback(() => {
    setLoading(true);
    setErr(null);
    // `.catch` only sees promise rejections. If `api.criticalResults` is absent — a bundle that
    // predates the endpoint, or a partial api object — the property access throws synchronously
    // and the rejection escapes this effect, unmounting the whole report editor rather than just
    // this panel. Degrade to an empty, error-labelled panel instead.
    const list = api.criticalResults?.list;
    if (typeof list !== 'function') {
      setItems([]);
      setErr('Critical results are unavailable in this build.');
      setLoading(false);
      return;
    }
    list
      .call(api.criticalResults, { reportId })
      .then((rows) => setItems(rows))
      .catch((e: Error) => setErr(e.message))
      .finally(() => setLoading(false));
  }, [reportId]);

  useEffect(() => {
    load();
  }, [load]);

  const replace = (updated: CriticalResult) =>
    setItems((prev) => prev.map((c) => (c.id === updated.id ? updated : c)));

  async function runAction(id: string, fn: () => Promise<CriticalResult>) {
    setBusyId(id);
    setActionErr(null);
    try {
      replace(await fn());
    } catch (e) {
      setActionErr((e as Error).message);
    } finally {
      setBusyId(null);
    }
  }

  async function onCreate() {
    const text = summary.trim();
    if (text.length === 0) return;
    setCreating(true);
    setActionErr(null);
    try {
      const created = await api.criticalResults.create({
        reportId,
        criticality,
        findingSummary: text,
      });
      setItems((prev) => [created, ...prev]);
      setSummary('');
      setCriticality('Red');
    } catch (e) {
      setActionErr((e as Error).message);
    } finally {
      setCreating(false);
    }
  }

  async function onCommunicate(id: string) {
    const recipient = communicatedTo.trim();
    if (recipient.length === 0) return;
    await runAction(id, () =>
      api.criticalResults.communicate(id, { communicatedTo: recipient, method }),
    );
    setCommunicatingId(null);
    setCommunicatedTo('');
    setMethod('Phone');
  }

  // Dismissal hides the panel ONLY while the report has nothing logged. A
  // report that already has a critical result keeps showing it no matter what
  // was dismissed: those rows carry an open communication loop (who was told,
  // whether they read back) and a stale dismissal must never be able to hide
  // clinical state that still needs closing. `loading`/`err` also keep the
  // panel up so a dismissal can't silently swallow a failed fetch.
  if (dismissed && !loading && !err && items.length === 0) return null;

  return (
    <div className="rp-panel" aria-live="polite" aria-busy={loading}>
      <div className="rp-critpanel-head">
        <div className="rp-panel-title">Critical results</div>
        <button
          type="button"
          className="rp-critpanel-dismiss"
          onClick={() => { setDismissed(true); writeDismissed(reportId, true); }}
          aria-label="Hide critical results for this report"
          title="Hide for this report"
        >
          <X size={15} aria-hidden />
        </button>
      </div>
      <p className="rp-page-sub" style={{ marginBottom: 12 }}>
        Log a critical finding, record who you told and how, and capture their read-back. RadioPad
        never contacts anyone for you.
      </p>

      {actionErr && (
        <p className="rp-page-sub" role="alert" style={{ marginBottom: 10 }}>
          <span className="badge danger">Action failed</span> {actionErr}
        </p>
      )}

      {loading ? (
        <TableSkeleton rows={2} cols={4} />
      ) : err ? (
        <ErrorState title="Couldn't load critical results" message={err} onRetry={load} />
      ) : items.length === 0 ? (
        <EmptyState
          title="No critical results on this report"
          description="If you find something that needs urgent communication, log it here so the loop is tracked and auditable."
        />
      ) : (
        <ul style={{ listStyle: 'none', margin: 0, padding: 0, display: 'grid', gap: 10 }}>
          {items.map((c) => {
            const busy = busyId === c.id;
            return (
              <li
                key={c.id}
                className="rp-stat-tile"
                style={{ display: 'grid', gap: 6 }}
                data-testid="critical-result-row"
              >
                <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6, alignItems: 'center' }}>
                  <span className={`badge ${CRITICALITY_BADGE_TONE[c.criticality]}`}>
                    {CRITICALITY_LABELS[c.criticality]}
                  </span>
                  <span className={`badge ${CRITICAL_STATUS_BADGE_TONE[c.status]}`}>{c.status}</span>
                  {isOpenLoop(c) && (
                    <span className={`badge ${c.isOverdue ? 'danger' : 'info'}`}>
                      {formatDueIn(c.dueAt, now)}
                    </span>
                  )}
                </div>

                <div>{c.findingSummary}</div>

                {c.communicatedTo && (
                  <div className="rp-page-sub">
                    Communicated to {c.communicatedTo}
                    {c.communicationMethod
                      ? ` by ${COMMUNICATION_METHOD_LABELS[c.communicationMethod].toLowerCase()}`
                      : ''}
                    {c.acknowledgedBy ? ` · acknowledged by ${c.acknowledgedBy}` : ''}
                  </div>
                )}

                {canManage && isOpenLoop(c) && (
                  <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
                    {c.status !== 'Communicated' && (
                      <button
                        type="button"
                        className="primary-ghost"
                        disabled={busy}
                        onClick={() =>
                          setCommunicatingId((prev) => (prev === c.id ? null : c.id))
                        }
                      >
                        Record communication
                      </button>
                    )}
                    <button
                      type="button"
                      className="ghost"
                      disabled={busy || c.communicatedAt === null}
                      title={
                        c.communicatedAt === null
                          ? 'Record the communication before acknowledging it'
                          : undefined
                      }
                      onClick={() =>
                        runAction(c.id, () => api.criticalResults.acknowledge(c.id))
                      }
                    >
                      Acknowledge
                    </button>
                    <button
                      type="button"
                      className="subtle"
                      disabled={busy}
                      onClick={() => runAction(c.id, () => api.criticalResults.close(c.id))}
                    >
                      Close
                    </button>
                  </div>
                )}

                {canManage && communicatingId === c.id && (
                  <div style={{ display: 'grid', gap: 8, marginTop: 4 }}>
                    <div className="section-block" style={{ marginBottom: 0 }}>
                      <label htmlFor={`cr-to-${c.id}`}>Communicated to</label>
                      <input
                        id={`cr-to-${c.id}`}
                        value={communicatedTo}
                        onChange={(e) => setCommunicatedTo(e.target.value)}
                        placeholder="Dr Osei (ED)"
                      />
                    </div>
                    <div className="section-block" style={{ marginBottom: 0 }}>
                      <label htmlFor={`cr-method-${c.id}`}>How</label>
                      <select
                        id={`cr-method-${c.id}`}
                        value={method}
                        onChange={(e) =>
                          setMethod(e.target.value as CriticalCommunicationMethod)
                        }
                      >
                        {METHOD_OPTIONS.map((m) => (
                          <option key={m} value={m}>
                            {COMMUNICATION_METHOD_LABELS[m]}
                          </option>
                        ))}
                      </select>
                    </div>
                    <div style={{ display: 'flex', gap: 6 }}>
                      <button
                        type="button"
                        className="primary"
                        disabled={busy || communicatedTo.trim().length === 0}
                        onClick={() => onCommunicate(c.id)}
                      >
                        Save communication
                      </button>
                      <button
                        type="button"
                        className="ghost"
                        onClick={() => setCommunicatingId(null)}
                      >
                        Cancel
                      </button>
                    </div>
                  </div>
                )}
              </li>
            );
          })}
        </ul>
      )}

      {canManage && (
        <div style={{ marginTop: 16 }}>
          <div className="rp-panel-title">Log a critical result</div>
          <div className="section-block">
            <label htmlFor="cr-criticality">Criticality</label>
            <select
              id="cr-criticality"
              value={criticality}
              onChange={(e) => setCriticality(e.target.value as CriticalityLevel)}
            >
              {CRITICALITY_OPTIONS.map((level) => (
                <option key={level} value={level}>
                  {CRITICALITY_LABELS[level]}
                </option>
              ))}
            </select>
          </div>
          <div className="section-block">
            <label htmlFor="cr-summary">Finding</label>
            <textarea
              id="cr-summary"
              value={summary}
              onChange={(e) => setSummary(e.target.value)}
              placeholder="Large right pneumothorax with mediastinal shift"
            />
          </div>
          <button
            type="button"
            className="primary"
            disabled={creating || summary.trim().length === 0}
            onClick={onCreate}
          >
            {creating ? 'Logging…' : 'Log critical result'}
          </button>
        </div>
      )}
    </div>
  );
}
