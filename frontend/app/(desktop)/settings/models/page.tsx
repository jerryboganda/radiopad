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
import { ArrowLeft, Cpu } from 'lucide-react';
import Container from '@/components/shell/Container';
import OnDeviceModels from '@/components/models/OnDeviceModels';

export default function DesktopOnDeviceModelsPage() {
  return (
    <Container>
      <div className="rp-model-hero">
        <span className="rp-model-hero-icon" aria-hidden>
          <Cpu size={26} strokeWidth={1.8} />
        </span>
        <div className="rp-model-hero-text">
          <h1 className="rp-page-title">On-device models</h1>
          <p className="rp-page-sub">
            Run dictation and optional offline report formatting directly on this workstation.
            Audio and dictation never leave the machine.
          </p>
        </div>
        <Link href="/settings" className="ghost rp-model-hero-back">
          <ArrowLeft aria-hidden size={16} /> Settings
        </Link>
      </div>
      <OnDeviceModels />
    </Container>
  );
}
