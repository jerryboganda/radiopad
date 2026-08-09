import React, { Suspense } from 'react';
import DictateClient from './DictateClient';

export default function DictatePage() {
  return (
    <Suspense
      fallback={
        <div className="py-16 text-center text-slate-400">
          <p className="text-sm">Loading dictation session...</p>
        </div>
      }
    >
      <DictateClient />
    </Suspense>
  );
}
