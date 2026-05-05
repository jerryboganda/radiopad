'use client';

import { useState } from 'react';
import Link from 'next/link';
import { api } from '@/lib/api';

type ImportSuccess = { reportId: string; status: string; warnings?: string[] };

export default function FhirImportPage() {
  const [body, setBody] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<ImportSuccess | null>(null);

  async function importJson() {
    setError(null);
    setResult(null);
    let trimmed = body.trim();
    if (!trimmed) {
      setError('Paste a FHIR DiagnosticReport JSON document first.');
      return;
    }
    try {
      JSON.parse(trimmed);
    } catch (e) {
      setError(`Body is not valid JSON: ${(e as Error).message}`);
      return;
    }
    setBusy(true);
    try {
      const res = await api.fhir.importDiagnosticReport(trimmed);
      setResult(res);
      setBody('');
    } catch (e) {
      const err = e as { body?: { error?: string }; message: string };
      setError(err.body?.error || err.message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="rp-container">
      <h1 className="rp-page-title">FHIR import</h1>
      <p className="rp-page-sub">
        Paste a <code>DiagnosticReport</code> resource (FHIR R4 JSON) to create
        a new RadioPad draft. Imported drafts inherit the current tenant and
        the AI-highlight policy.
      </p>

      {error && <div className="banner warn">{error}</div>}
      {result && (
        <div className="banner info">
          Imported as <code>{result.status}</code> · draft{' '}
          <Link href={`/reports/${result.reportId}`}>
            <code>{result.reportId}</code>
          </Link>
          {result.warnings && result.warnings.length > 0 && (
            <ul className="rp-list rp-mt-sm">
              {result.warnings.map((w, i) => (
                <li key={i} className="rp-divider-row">{w}</li>
              ))}
            </ul>
          )}
        </div>
      )}

      <div className="rp-panel">
        <div className="rp-panel-title">DiagnosticReport JSON</div>
        <div className="composer-shell">
          <textarea
            className="rp-input"
            rows={20}
            placeholder='{ "resourceType": "DiagnosticReport", ... }'
            value={body}
            onChange={(e) => setBody(e.target.value)}
            spellCheck={false}
          />
        </div>
        <div className="rp-toolbar rp-mt-sm">
          <button className="primary" disabled={busy} onClick={importJson}>
            {busy ? 'Importing…' : 'Import'}
          </button>
          <button className="ghost" disabled={busy || !body} onClick={() => setBody('')}>
            Clear
          </button>
        </div>
      </div>
    </div>
  );
}
