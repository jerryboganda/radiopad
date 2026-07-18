'use client';

/**
 * F8 — one-command "Sign & Send". Applies the radiologist's Primary sign-off, acknowledges the
 * report (the server blocks this if validation blockers remain), then exports — chaining the
 * existing gated endpoints via `api.reports.signAndSend`. Nothing is auto-signed: the two-click
 * confirm IS the explicit verification action, and if sign/acknowledge fails nothing is exported.
 */

import { useState } from 'react';
import { api } from '@/lib/api';
import Banner from '@/components/ui/Banner';

type ExportFmt = 'text' | 'fhir' | 'hl7' | 'json';

export default function SignAndSendButton({ reportId }: { reportId: string }) {
  const [format, setFormat] = useState<ExportFmt>('text');
  const [busy, setBusy] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);

  async function run() {
    setBusy(true);
    setError(null);
    try {
      await api.reports.signAndSend(reportId, { format });
      setDone(true);
      setConfirming(false);
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
        One action: apply your <strong>Primary sign-off</strong>, acknowledge (the server rejects this
        if validation blockers remain), then export. Nothing is auto-signed — confirming below is your
        explicit verification.
      </p>

      {error && <Banner tone="danger" onDismiss={() => setError(null)}>{error}</Banner>}
      {done && <Banner tone="success" title="Signed, acknowledged & exported." />}

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
