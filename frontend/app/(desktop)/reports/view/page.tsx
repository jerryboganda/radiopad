'use client';

import { Suspense } from 'react';
import { useSearchParams } from 'next/navigation';
import ReportClient from '../[id]/ReportClient';
import DictationOverlay from '@/components/dictation/DictationOverlay';

/**
 * Keyed on `?id=` so moving to another report on this same route ("Next case", the
 * case-queue rail) remounts the editor. ReportClient reads the id once on mount and
 * holds per-report state throughout; without the key the previous report stayed on
 * screen under the new URL.
 */
function KeyedReport() {
  const id = useSearchParams()?.get('id') ?? '';
  return <ReportClient key={id} />;
}

export default function ReportViewPage() {
  return (
    <>
      <Suspense fallback={null}>
        <KeyedReport />
      </Suspense>
      {/* Floating Dictate/HQ/Fix mic. Scoped to the report editor only — it is
          deliberately NOT in the root layout, so it never appears on other
          screens (AI models, rulebooks, etc.). */}
      <DictationOverlay />
    </>
  );
}
