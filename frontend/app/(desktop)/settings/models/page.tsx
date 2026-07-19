'use client';

/**
 * On-device AI models — the DESKTOP surface's model manager.
 *
 * This screen did not exist. The manager component lived only under `app/(web)/providers/`, and
 * `build-surface.mjs` stages non-target route groups out of `app/`, so the desktop bundle never
 * shipped it — while the desktop is the ONLY surface where these engines actually run. Downloading
 * MedASR, making it primary, testing it, freeing disk: none of it was reachable from the product
 * that uses it. Several error messages (dictation, the phone companion, the offline formatter)
 * already told radiologists to "open Settings → On-device models", a screen that was not there.
 *
 * The manager component now lives in `components/models/` so both surfaces render the same UI: web
 * for platform operators inspecting a workstation's state, desktop for the radiologist who has to
 * download the model.
 */

import Link from 'next/link';
import { ArrowLeft, HardDriveDownload } from 'lucide-react';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import OnDeviceModels from '@/components/models/OnDeviceModels';

export default function DesktopOnDeviceModelsPage() {
  return (
    <Container>
      <PageHeader
        title={<><HardDriveDownload aria-hidden size={20} /> On-device models</>}
        description="Speech-to-text and the optional offline report formatter, running entirely on this workstation. Audio and dictation never leave the machine."
        secondaryActions={
          <Link href="/settings" className="ghost">
            <ArrowLeft aria-hidden size={16} /> Settings
          </Link>
        }
      />
      <OnDeviceModels />
    </Container>
  );
}
