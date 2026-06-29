'use client';

import { useCallback, useEffect, useState } from 'react';
import { api } from '@/lib/api';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import Skeleton from '@/components/ui/Skeleton';
import ErrorState from '@/components/ui/ErrorState';
import AnimatedNumber from '@/components/ui/AnimatedNumber';

type AuditEvent = { action: number | string; createdAt: string; detailsJson: string };

export default function GovernancePage() {
  const [events, setEvents] = useState<AuditEvent[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const load = useCallback(() => {
    setLoading(true);
    setError(null);
    api.audit.query({ take: 500 })
      .then((e) => setEvents(e as AuditEvent[]))
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  const counts = events.reduce<Record<number, number>>((acc, e) => {
    const n = typeof e.action === 'number' ? e.action : -1;
    acc[n] = (acc[n] || 0) + 1;
    return acc;
  }, {});

  const aiRequests = counts[0] || 0;
  const aiResponses = counts[1] || 0;
  const exports = counts[3] || 0;
  const acknowledged = counts[4] || 0;
  const policyViolations = counts[9] || 0;
  const rulebookApproved = counts[6] || 0;
  const rulebookDeprecated = counts[7] || 0;

  return (
    <Container>
      <PageHeader
        title="Governance dashboard"
        description="Tenant-wide oversight derived from the append-only audit log. Showing the most recent 500 events."
      />

      {error && <ErrorState message={error} onRetry={load} />}

      {!error && (
        <div
          className="rp-workspace rp-stagger"
          style={{ gridTemplateColumns: 'repeat(3, 1fr)' }}
          aria-live="polite"
          aria-busy={loading}
        >
          <div className="rp-panel">
            <div className="rp-panel-title">AI activity</div>
            {loading ? (
              <StatSkeleton rows={3} />
            ) : (
              <>
                <Stat label="Requests" value={aiRequests} />
                <Stat label="Responses" value={aiResponses} />
                <Stat label="Policy violations" value={policyViolations} tone={policyViolations > 0 ? 'danger' : 'ok'} />
              </>
            )}
          </div>
          <div className="rp-panel">
            <div className="rp-panel-title">Reporting</div>
            {loading ? (
              <StatSkeleton rows={2} />
            ) : (
              <>
                <Stat label="Acknowledged" value={acknowledged} tone="ok" />
                <Stat label="Exported" value={exports} />
              </>
            )}
          </div>
          <div className="rp-panel">
            <div className="rp-panel-title">Rulebook lifecycle</div>
            {loading ? (
              <StatSkeleton rows={2} />
            ) : (
              <>
                <Stat label="Approvals" value={rulebookApproved} tone="ok" />
                <Stat label="Deprecations" value={rulebookDeprecated} tone="warn" />
              </>
            )}
          </div>
        </div>
      )}

      <div className="rp-panel rp-anim-fade-in-up">
        <div className="rp-panel-title">PHI routing legend</div>
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
          <span className="badge ok">PHI-approved</span>
          <span className="badge ai">Local-only</span>
          <span className="badge info">De-identified only</span>
          <span className="badge warn">Sandbox</span>
          <span className="badge danger">Blocked</span>
        </div>
        <p style={{ color: 'var(--text-muted)', fontSize: 12, marginTop: 12 }}>
          Routing decisions are enforced in the backend AI gateway. Violations appear above as{' '}
          <code>PolicyViolation</code> audit events.
        </p>
      </div>
    </Container>
  );
}

function Stat({ label, value, tone }: { label: string; value: number; tone?: 'ok' | 'warn' | 'danger' }) {
  return (
    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', padding: '6px 0', borderBottom: '1px solid var(--border-soft)' }}>
      <span style={{ color: 'var(--text-muted)', fontSize: 12, textTransform: 'uppercase', letterSpacing: '0.06em' }}>{label}</span>
      <span className={tone ? `badge ${tone}` : ''} style={{ fontFamily: 'var(--serif)', fontSize: 18 }}>
        <AnimatedNumber value={value} />
      </span>
    </div>
  );
}

function StatSkeleton({ rows }: { rows: number }) {
  return (
    <>
      {Array.from({ length: rows }).map((_, i) => (
        <div
          key={i}
          style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', padding: '6px 0', borderBottom: '1px solid var(--border-soft)' }}
        >
          <Skeleton variant="text" width="45%" />
          <Skeleton variant="text" width={28} />
        </div>
      ))}
    </>
  );
}
