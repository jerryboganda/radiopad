'use client';

/**
 * F8 — one-command "Sign & Send". Validates FIRST, then applies the radiologist's Primary sign-off,
 * acknowledges, then exports — chaining the existing gated endpoints via `api.reports.signAndSend`.
 * Nothing is auto-signed: the two-click confirm IS the explicit verification action.
 *
 * The validate-first ordering matters: `sign` performs no blocker check (only `acknowledge` does),
 * so signing first left blocker-laden reports permanently signed after a failed acknowledge.
 */

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { api } from '@/lib/api';
import { reportHref } from '@/lib/routes';
import { pickNextCase } from '@/lib/worklist/smartAssign';
import { getSessionReads } from '@/lib/worklist/readerLoad';
import Banner from '@/components/ui/Banner';

type ExportFmt = 'text' | 'fhir' | 'hl7' | 'json';

export default function SignAndSendButton({
  reportId,
  beforeRun,
  onDone,
}: {
  reportId: string;
  /** Save on-screen edits first, so what gets signed is what the radiologist sees. Throws → nothing is signed. */
  beforeRun?: () => Promise<void>;
  /** Let the editor refresh its status badges and signatures. */
  onDone?: () => void;
}) {
  const [format, setFormat] = useState<ExportFmt>('text');
  const [busy, setBusy] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);
  const router = useRouter();

  /** Open the worklist's suggested next read (STAT first, then longest wait), or the worklist when the queue is empty. */
  async function nextCase() {
    try {
      const next = pickNextCase(await api.reports.list(), reportId, { history: getSessionReads() });
      router.push(next ? reportHref(next.id) : '/worklist');
    } catch {
      router.push('/worklist');
    }
  }

  async function run() {
    setBusy(true);
    setError(null);
    try {
      await beforeRun?.();
      await api.reports.signAndSend(reportId, { format });
      setDone(true);
      setConfirming(false);
      onDone?.();
    } catch (e) {
      const err = e as { body?: { error?: string; detail?: string }; message: string };
      setError(err.body?.error || err.body?.detail || err.message || 'Sign & Send failed.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="rp-panel rp-anim-scale-in">
      <div className="rp-panel-title">Sign &amp; Send</div>
      <p className="rp-page-sub">
        One action: check for validation blockers, apply your <strong>Primary sign-off</strong>,
        acknowledge, then export. If any blockers remain, nothing is signed. Nothing is auto-signed —
        confirming below is your explicit verification.
      </p>

      {error && <Banner tone="danger" onDismiss={() => setError(null)}>{error}</Banner>}
      {done && <Banner tone="success" title="Signed, acknowledged & exported." />}
      {done && (
        <button className="primary" type="button" onClick={() => { void nextCase(); }}>
          Next case
        </button>
      )}

      <div className="rp-toolbar">
        <label htmlFor="rp-sas-format" className="rp-page-sub">Export as</label>
        <select
          id="rp-sas-format"
          className="rp-input"
          value={format}
          onChange={(e) => setFormat(e.target.value as ExportFmt)}
          disabled={busy || done}
        >
          <option value="text">Text</option>
          <option value="fhir">FHIR R4</option>
          <option value="hl7">HL7 v2 (ORU)</option>
          <option value="json">JSON</option>
        </select>

        {!confirming ? (
          <button className="primary" type="button" onClick={() => setConfirming(true)} disabled={busy || done}>
            Sign &amp; Send
          </button>
        ) : (
          <>
            <button className="primary" type="button" onClick={run} disabled={busy} aria-busy={busy}>
              {busy && <span className="rp-spinner sm" aria-hidden />}
              {busy ? 'Working…' : 'Confirm sign-off'}
            </button>
            <button className="ghost" type="button" onClick={() => setConfirming(false)} disabled={busy}>
              Cancel
            </button>
          </>
        )}
      </div>
    </div>
  );
}
