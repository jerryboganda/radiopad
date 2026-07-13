'use client';

/**
 * Desktop home — RC IA makes the Dashboard the landing module (RC-01 nav).
 * The old rich worklist that lived here is superseded by /worklist (RC case
 * queue) and /reports (report archive). Web/mobile builds swap this root out
 * entirely (scripts/build-surface.mjs), so this redirect is desktop-only.
 */

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import Skeleton from '@/components/ui/Skeleton';

export default function Home() {
  const router = useRouter();
  useEffect(() => {
    router.replace('/dashboard');
  }, [router]);
  return (
    <div style={{ display: 'grid', gap: 12, maxWidth: 720, margin: '48px auto' }} aria-busy="true">
      <Skeleton height={32} width="40%" />
      <Skeleton variant="block" height={120} />
      <Skeleton variant="block" height={120} />
    </div>
  );
}
