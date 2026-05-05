'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { api, type Report } from '@/lib/api';

export default function ReportsPage() {
  const [reports, setReports] = useState<Report[]>([]);

  useEffect(() => { api.reports.list().then(setReports).catch(() => setReports([])); }, []);

  return (
    <>
      <h1 className="page-title">Reports</h1>
      <p className="page-sub">All reports for the active tenant.</p>
      <div className="panel">
        <table>
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
                <td><Link href={`/reports/${r.id}`}>Open →</Link></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  );
}

function statusLabel(s: Report['status']): string {
  if (typeof s === 'string') return s;
  return ['Draft', 'Validated', 'Acknowledged', 'Exported'][s] ?? String(s);
}
