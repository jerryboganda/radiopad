'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { api, type Report } from '@/lib/api';
import { reportHref } from '@/lib/routes';

export default function ReportsPage() {
  const [reports, setReports] = useState<Report[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api.reports.list()
      .then(setReports)
      .catch((e) => setError(e?.message || 'Failed to load'))
      .finally(() => setLoading(false));
  }, []);

  return (
    <>
      <h1 className="rp-page-title">Reports</h1>
      <p className="rp-page-sub">All radiology reports in your workspace.</p>
      {error && <div className="banner warn">{error}</div>}
      {loading ? (
        <div className="rp-page-sub">Loading…</div>
      ) : (
      <div className="rp-panel">
        <table className="rp-table">
          <thead>
            <tr><th>Accession</th><th>Modality</th><th>Body part</th><th>Status</th><th>Updated</th><th></th></tr>
          </thead>
          <tbody>
            {reports.map((r) => (
              <tr key={r.id}>
                <td>{r.study.accessionNumber}</td>
                <td>{r.study.modality}</td>
                <td>{r.study.bodyPart}</td>
                <td><span className="badge">{statusLabel(r.status)}</span></td>
                <td className="muted">{new Date(r.updatedAt).toLocaleString()}</td>
                <td><Link href={reportHref(r.id)}>Open →</Link></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      )}
    </>
  );
}

function statusLabel(s: Report['status']): string {
  if (typeof s === 'string') return s;
  return ['Draft', 'Validated', 'Acknowledged', 'Exported'][s] ?? String(s);
}
