import NewReportWizard from './NewReportWizard';
import DictationOverlay from '@/components/dictation/DictationOverlay';

export default function NewReportPage() {
  return (
    <>
      <NewReportWizard />
      {/* Floating Dictate / HQ mic so the radiologist can voice the findings +
          history into the rich editors during intake (same overlay the report
          editor uses; it targets whichever rich field was last focused). */}
      <DictationOverlay />
    </>
  );
}
