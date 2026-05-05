'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { api, type Report, type ValidationResult } from '@/lib/api';

type Row = {
  report: Report;
  result: ValidationResult | null;
  err?: string;
};

/**
 * PRD §16.1 Validation Center — bird's-eye view of validation status across
 * the tenant's draft reports. Re-runs `POST /api/reports/{id}/validate` for
 * each row and aggregates findings by severity (Blocker / Warning / Info)
 * so the medical director can spot systemic issues quickly.
 *
 * Validation is read-only relative to the report; it never mutates the
 * draft. All queries scope by tenant via the API client.
 */
export default function ValidationCenterPage() {
  const [rows, setRows] = useState<Row[]>([]);
  const [busy, setBusy] = useState(true);
  const [err, setErr] = useState<string | null>(null);

  async function refresh() {
    setBusy(true); setErr(null);
    try {
      const reports = await api.reports.list();
      const drafts = reports.filter((r) => {
        const s = typeof r.status === 'string' ? r.status : ['Draft', 'Validated', 'Acknowledged', 'Exported'][r.status as number];
        return s !== 'Exported';
      });
      const out: Row[] = [];
      for (const r of drafts) {
        try {
          const result = await api.reports.validate(r.id);
          out.push({ report: r, result });
        } catch (e) {
          out.push({ report: r, result: null, err: (e as Error).message });
        }
      }
      setRows(out);
    } catch (e) {
      setErr((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  useEffect(() => { refresh(); }, []);

  const totals = rows.reduce(
    (acc, r) => {
      const f = r.result?.findings ?? [];
      for (const x of f) {
        const sev = (typeof x.severity === 'string'
          ? x.severity
          : ['Info', 'Warning', 'Blocker'][x.severity as number] ?? 'Info').toLowerCase();
        if (sev === 'blocker') acc.blockers++;
        else if (sev === 'warning') acc.warnings++;
        else acc.infos++;
      }
      return acc;
    },
    { blockers: 0, warnings: 0, infos: 0 },
  );

  return (
    <div className="rp-container">
      <h1 className="rp-page-title">Validation Center</h1>
      <p className="rp-page-sub">
        Re-runs the validation engine over every non-exported draft. Severities
        map to the locked semantic families: <span className="badge danger">blocker → red</span>{' '}
        <span className="badge warn">warning → amber</span>{' '}
        <span className="badge info">info → blue</span>.
      </p>

      {err && <div className="banner warn">{err}</div>}

      <div className="rp-panel">
        <div className="rp-panel-title">Summary</div>
        <div className="rp-grid-3">
          <div className="rp-panel" style={{ padding: 16 }}>
            <div className="rp-page-sub">Reports scanned</div>
            <div style={{ fontSize: 28, fontFamily: 'var(--serif)' }}>{rows.length}</div>
          </div>
          <div className="rp-panel" style={{ padding: 16 }}>
            <div className="rp-page-sub">Blockers</div>
            <div style={{ fontSize: 28, fontFamily: 'var(--serif)' }}>
              {totals.blockers}{' '}
              <span className={`badge ${totals.blockers > 0 ? 'danger' : 'ok'}`}>
                {totals.blockers > 0 ? 'attention' : 'clean'}
              </span>
            </div>
          </div>
          <div className="rp-panel" style={{ padding: 16 }}>
            <div className="rp-page-sub">Warnings / info</div>
            <div style={{ fontSize: 28, fontFamily: 'var(--serif)' }}>
              {totals.warnings} / {totals.infos}
            </div>
          </div>
        </div>
        <div style={{ marginTop: 12 }}>
          <button className="ghost" onClick={refresh} disabled={busy}>
            {busy ? 'Re-validating…' : 'Re-run validation'}
          </button>
        </div>
      </div>

      <div className="rp-panel">
        <div className="rp-panel-title">Reports</div>
        <table className="rp-table">
          <thead>
            <tr>
              <th>Accession</th>
              <th>Modality</th>
              <th>Body part</th>
              <th>Findings</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {rows.length === 0 && !busy && (
              <tr><td colSpan={5} style={{ color: 'var(--text-muted)' }}>No drafts to validate.</td></tr>
            )}
            {rows.map(({ report, result, err: e }) => {
              const findings = result?.findings ?? [];
              const blockers = findings.filter((f) => {
                const s = typeof f.severity === 'string' ? f.severity : ['Info','Warning','Blocker'][f.severity as number];
                return s === 'Blocker';
              }).length;
              const warnings = findings.filter((f) => {
                const s = typeof f.severity === 'string' ? f.severity : ['Info','Warning','Blocker'][f.severity as number];
                return s === 'Warning';
              }).length;
              return (
                <tr key={report.id}>
                  <td><code>{report.study.accessionNumber || '—'}</code></td>
                  <td>{report.study.modality}</td>
                  <td>{report.study.bodyPart}</td>
                  <td>
                    {e
                      ? <span className="badge warn">{e}</span>
                      : (
                        <>
                          {blockers > 0 && <span className="badge danger">{blockers} blocker</span>}{' '}
                          {warnings > 0 && <span className="badge warn">{warnings} warning</span>}{' '}
                          {findings.length === 0 && <span className="badge ok">clean</span>}
                        </>
                      )}
                  </td>
                  <td><Link href={`/reports/${report.id}`}>Open →</Link></td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}
