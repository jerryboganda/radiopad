'use client';

import { useEffect, useState } from 'react';
import { api } from '@/lib/api';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import ErrorState from '@/components/ui/ErrorState';

type AuditEvent = {
  id: string;
  action: number | string;
  integrityChain: string;
  createdAt: string;
};

type Verdict = Awaited<ReturnType<typeof api.audit.verify>>;

export default function AuditVerifyPage() {
  // Display only: the verdict comes from the server, which walks the tenant's WHOLE chain. A
  // browser recompute over a recent window can't — row 1 of a window has an unknown predecessor, so
  // any tenant with more events than the window would see a false "broken" row.
  const [events, setEvents] = useState<AuditEvent[]>([]);
  const [verdict, setVerdict] = useState<Verdict | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api.audit.query({ take: 500 })
      .then((e) => setEvents(e as AuditEvent[]))
      .catch((e: Error) => setError(e.message));
  }, []);

  async function verify() {
    setBusy(true);
    setError(null);
    try {
      setVerdict(await api.audit.verify());
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <Container>
      <PageHeader
        title="Audit chain verifier"
        description={
          <>
            Asks the server to recompute the SHA-256 hash chain over this workspace&apos;s entire audit
            log. Tampering with any row breaks the chain at that row and at every row after it.
          </>
        }
      />

      {error && <ErrorState message={error} onRetry={verify} />}

      <div className="rp-toolbar">
        <button className="primary" onClick={verify} disabled={busy} aria-busy={busy}>
          {busy && <span className="rp-spinner sm" aria-hidden />}
          {busy ? 'Verifying…' : 'Verify audit chain'}
        </button>
        <span aria-live="polite">
          {verdict && (
            verdict.intact
              ? <span className="badge ok">Chain intact ({verdict.eventCount} events)</span>
              : <span className="badge danger">Chain broken — {verdict.eventCount} events checked</span>
          )}
        </span>
      </div>

      {events.length > 0 && (
        <div className="rp-panel rp-anim-fade-in-up">
          <div className="rp-panel-title">Latest {events.length} events</div>
          <table className="rp-table">
            <thead>
              <tr><th>Event</th><th>When</th><th>Chain hash</th><th>Status</th></tr>
            </thead>
            <tbody>
              {events.map((r) => (
                <tr key={r.id}>
                  <td><code style={{ fontSize: 11 }}>{r.id.slice(0, 8)}…</code></td>
                  <td>{new Date(r.createdAt).toLocaleString()}</td>
                  <td><code style={{ fontSize: 11 }}>{r.integrityChain.slice(0, 12)}…</code></td>
                  <td>
                    {verdict && !verdict.intact && verdict.firstBrokenEventId === r.id
                      ? <span className="badge danger">first broken row</span>
                      : null}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Container>
  );
}
