'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { api, type Report, type ValidationResult } from '@/lib/api';
import { reportHref } from '@/lib/routes';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import Banner from '@/components/ui/Banner';
import ErrorState from '@/components/ui/ErrorState';
import { TableSkeleton } from '@/components/ui/Skeleton';
import EmptyState from '@/components/ui/EmptyState';
import AnimatedNumber from '@/components/ui/AnimatedNumber';

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
    <Container>
      <PageHeader
        title="Quality check"
        description={<>Spots problems in your team&apos;s draft reports — things like missing key findings or contradictions. Severity: <span className="badge danger">stop — must fix</span> <span className="badge warn">should review</span> <span className="badge info">heads-up</span>.</>}
      />

      {err && <Banner tone="warn">{err}</Banner>}

      <div className="rp-panel">
        <div className="rp-panel-title">Summary</div>
        <div className="metric-grid rp-stagger" aria-live="polite" aria-busy={busy}>
          <div className="metric-card" data-tone="info">
            <div className="metric-card-value"><AnimatedNumber value={rows.length} /></div>
            <div className="metric-card-label">Drafts checked</div>
          </div>
          <div className="metric-card" data-tone={totals.blockers > 0 ? 'blocked' : 'ready'}>
            <div className="metric-card-value">
              <AnimatedNumber value={totals.blockers} />{' '}
              <span className={`badge ${totals.blockers > 0 ? 'danger' : 'ok'}`}>
                {totals.blockers > 0 ? 'needs attention' : 'all clear'}
              </span>
            </div>
            <div className="metric-card-label">Must-fix issues</div>
          </div>
          <div className="metric-card" data-tone="review">
            <div className="metric-card-value">
              <AnimatedNumber value={totals.warnings} /> / <AnimatedNumber value={totals.infos} />
            </div>
            <div className="metric-card-label">Warnings / heads-up</div>
          </div>
        </div>
        <div style={{ marginTop: 12 }}>
          <button className="ghost" onClick={refresh} disabled={busy} aria-busy={busy}>
            {busy && <span className="rp-spinner sm" aria-hidden />}
            {busy ? 'Re-checking…' : 'Re-run check'}
          </button>
        </div>
      </div>

      <div className="rp-panel" aria-live="polite" aria-busy={busy}>
        <div className="rp-panel-title">Drafts</div>
        {busy && rows.length === 0 ? (
          <TableSkeleton rows={6} cols={5} />
        ) : err && rows.length === 0 ? (
          <ErrorState title="Couldn't run the quality check" message={err} onRetry={refresh} />
        ) : rows.length === 0 ? (
          <EmptyState
            title="No drafts to check right now"
            description="Once your team has draft reports, their validation status will appear here."
          />
        ) : (
        <table className="rp-table">
          <thead>
            <tr>
              <th>Accession</th>
              <th>Modality</th>
              <th>Body part</th>
              <th>Issues found</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
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
                  <td>{report.study.accessionNumber || '—'}</td>
                  <td>{report.study.modality}</td>
                  <td>{report.study.bodyPart}</td>
                  <td>
                    {e
                      ? <span className="badge warn">{e}</span>
                      : (
                        <>
                          {blockers > 0 && <span className="badge danger">{blockers} must-fix</span>}{' '}
                          {warnings > 0 && <span className="badge warn">{warnings} warning</span>}{' '}
                          {findings.length === 0 && <span className="badge ok">all clear</span>}
                        </>
                      )}
                  </td>
                  <td><Link href={reportHref(report.id)}>Open →</Link></td>
                </tr>
              );
            })}
          </tbody>
        </table>
        )}
      </div>
    </Container>
  );
}
