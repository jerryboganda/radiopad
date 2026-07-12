'use client';

import { useState } from 'react';
import Link from 'next/link';
import { api } from '@/lib/api';
import { reportHref } from '@/lib/routes';
import Banner from '@/components/ui/Banner';

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
      setError('Paste the report data from your hospital system first.');
      return;
    }
    try {
      JSON.parse(trimmed);
    } catch (e) {
      setError(`That doesn't look like valid hospital system data: ${(e as Error).message}`);
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
      <header className="rp-page-header">
        <div className="rp-page-header-text">
          <h1 className="rp-page-title">Import from hospital system</h1>
          <p className="rp-page-sub">
            Bring an existing report from your hospital&apos;s system into RadioPad as a new draft.
          </p>
        </div>
      </header>

      <div className="rp-page-grid">
        <div className="rp-page-main">

      {error && <Banner tone="warn" onDismiss={() => setError(null)}>{error}</Banner>}
      {result && (
        <div className="rp-anim-fade-in-up">
          <Banner tone="success">
            Imported as a {result.status} draft —{' '}
            <Link href={reportHref(result.reportId)}>open it now</Link>
            {result.warnings && result.warnings.length > 0 && (
              <ul className="rp-list rp-mt-sm">
                {result.warnings.map((w, i) => (
                  <li key={i} className="rp-divider-row">{w}</li>
                ))}
              </ul>
            )}
          </Banner>
        </div>
      )}

      <div className="rp-panel">
        <div className="rp-panel-title">Paste report data</div>
        <p className="rp-page-sub">
          Ask your IT team to copy the report from the hospital system and paste it here. They&apos;ll know what format to use.
        </p>
        <div className="composer-shell">
          <textarea
            className="rp-input"
            rows={20}
            placeholder='Paste the report data here…'
            value={body}
            onChange={(e) => setBody(e.target.value)}
            spellCheck={false}
          />
        </div>
        <div className="rp-toolbar rp-mt-sm">
          <button className="primary" disabled={busy} onClick={importJson} aria-busy={busy}>
            {busy && <span className="rp-spinner sm" aria-hidden />}
            {busy ? 'Importing…' : 'Import as draft'}
          </button>
          <button className="ghost" disabled={busy || !body} onClick={() => setBody('')}>
            Clear
          </button>
        </div>
        <details className="rp-advanced">
          <summary>For IT teams — technical details</summary>
          <p className="rp-page-sub">
            Accepts a FHIR R4 <code>DiagnosticReport</code> resource as JSON.
            Imported drafts inherit the current workspace and the AI-highlight policy. See <code>docs/03-architecture/api-reference.md</code>.
          </p>
        </details>
      </div>

        </div>
        <aside className="rp-page-aside">
          <div className="rp-help">
            <div className="rp-help-title">What this does</div>
            <p>If a report already exists in another system (like your hospital&apos;s electronic record), this brings it into RadioPad as a new draft you can edit.</p>
          </div>
          <div className="rp-help">
            <div className="rp-help-title">Need help?</div>
            <p>Ask your hospital IT team. They&apos;ll know how to pull a report from your existing system in the right format.</p>
          </div>
        </aside>
      </div>
    </div>
  );
}
