import { Suspense } from 'react';
import Skeleton from '@/components/ui/Skeleton';
import DictateClient from './DictateClient';

export default function DictatePage() {
  return (
    <Suspense
      fallback={
        <div className="rp-mobile" aria-busy="true">
          <Skeleton variant="block" height={80} />
          <Skeleton variant="block" height={220} />
        </div>
      }
    >
      <DictateClient />
    </Suspense>
  );
}
