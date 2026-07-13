'use client';

import { useCallback, useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { api, type Report } from '@/lib/api';
import Container from '@/components/shell/Container';
import Skeleton from '@/components/ui/Skeleton';
import ErrorState from '@/components/ui/ErrorState';

/** True for reports still in Draft, whether the API sent the enum name or number. */
function isDraft(r: Report): boolean {
  return r.status === 'Draft' || r.status === 0;
}

/**
 * Report Composer entry point. This page never renders content of its own —
 * it looks up the most recently edited draft and drops the user straight into
 * the editor for it, or starts the new-report wizard when there are no drafts.
 */
export default function ReportComposerPage() {
  const router = useRouter();
  const [err, setErr] = useState<string | null>(null);

  const decide = useCallback(() => {
    setErr(null);
    let cancelled = false;
    api.reports
      .list()
      .then((reports) => {
        if (cancelled) return;
        const latestDraft = reports
          .filter(isDraft)
          .sort(
            (a, b) =>
              new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime(),
          )[0];
        if (latestDraft) {
          router.replace(`/reports/view?id=${encodeURIComponent(latestDraft.id)}`);
        } else {
          router.replace('/reports/new');
        }
      })
      .catch((e: Error) => {
        if (!cancelled) setErr(e.message);
      });
    return () => {
      cancelled = true;
    };
  }, [router]);

  useEffect(() => decide(), [decide]);

  return (
    <Container>
      {err ? (
        <ErrorState message={err} onRetry={decide} />
      ) : (
        <div
          aria-busy
          aria-live="polite"
          style={{
            minHeight: '50vh',
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            justifyContent: 'center',
            gap: 16,
          }}
        >
          <div style={{ width: 'min(420px, 90%)', display: 'grid', gap: 10 }}>
            <Skeleton variant="text" width="60%" />
            <Skeleton variant="text" width="100%" />
            <Skeleton variant="text" width="80%" />
          </div>
          <p className="rp-page-sub">Opening composer…</p>
        </div>
      )}
    </Container>
  );
}
