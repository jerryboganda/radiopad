import ReportClient from '../[id]/ReportClient';
import DictationOverlay from '@/components/dictation/DictationOverlay';

export default function ReportViewPage() {
  return (
    <>
      <ReportClient />
      {/* Floating Dictate/HQ/Fix mic. Scoped to the report editor only — it is
          deliberately NOT in the root layout, so it never appears on other
          screens (AI models, rulebooks, etc.). */}
      <DictationOverlay />
    </>
  );
}
