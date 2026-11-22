'use client';

/**
 * Legacy `/governance` — superseded by `/admin/governance` (the RBAC-gated dashboard).
 * Kept as a client redirect (static export has no server redirects) so old links keep working.
 */

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import Skeleton from '@/components/ui/Skeleton';

export default function GovernanceRedirect() {
  const router = useRouter();
  useEffect(() => {
    router.replace('/admin/governance/');
  }, [router]);
  return (
    <div style={{ display: 'grid', gap: 12, maxWidth: 720, margin: '48px auto' }} aria-busy="true">
      <Skeleton height={32} width="40%" />
      <Skeleton variant="block" height={120} />
    </div>
  );
}
