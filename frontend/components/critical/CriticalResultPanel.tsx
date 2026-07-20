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

  // Keep the countdown honest without re-fetching.
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), 30000);
    return () => clearInterval(t);
  }, []);

  const load = useCallback(() => {
    setLoading(true);
    setErr(null);
    api.criticalResults
      .list({ reportId })
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

  return (
    <div className="rp-panel" aria-live="polite" aria-busy={loading}>
      <div className="rp-panel-title">Critical results</div>
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
