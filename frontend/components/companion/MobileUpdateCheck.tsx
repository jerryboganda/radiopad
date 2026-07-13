'use client';

/**
 * "Check for updates" for the phone companion app.
 *
 * The desktop shell self-updates via Tauri (CheckUpdatesButton); a sideloaded
 * Android APK cannot, so this gives the companion the same affordance: tap to
 * check the latest GitHub release, and if it's newer than this build, offer the
 * APK to download + install. Renders ONLY in the mobile bundle (`isMobileSurface`
 * is a build-time constant, so it is dead-code-eliminated from desktop/web).
 */

import { useCallback, useState } from 'react';
import { isMobileSurface } from '@/lib/surface';
import {
  checkMobileUpdate,
  APP_VERSION,
  RELEASES_URL,
  type MobileUpdateInfo,
} from '@/lib/mobileUpdate';

type Phase = 'idle' | 'checking' | 'uptodate' | 'available' | 'error';

export default function MobileUpdateCheck() {
  const [phase, setPhase] = useState<Phase>('idle');
  const [info, setInfo] = useState<MobileUpdateInfo | null>(null);

  const check = useCallback(async () => {
    setPhase('checking');
    try {
      const result = await checkMobileUpdate();
      setInfo(result);
      setPhase(result.updateAvailable ? 'available' : 'uptodate');
    } catch {
      setPhase('error');
    }
  }, []);

  // Compiled away on desktop/web — those surfaces have their own update paths.
  if (!isMobileSurface) return null;

  if (phase === 'available' && info) {
    return (
      <div className="rp-mobile-update banner ok" role="status">
        <div>
          Update available: <strong>v{info.latest}</strong>
          <span className="rp-mobile-update-cur"> · you have v{info.current}</span>
        </div>
        {/* Capacitor opens external links in the system browser, which downloads
            the APK; tapping the downloaded file launches the Android installer. */}
        <a
          className="primary"
          href={info.downloadUrl ?? RELEASES_URL}
          target="_blank"
          rel="noreferrer"
        >
          {info.downloadUrl ? 'Download & install' : 'Open release page'}
        </a>
      </div>
    );
  }

  const label =
    phase === 'checking' ? 'Checking…'
      : phase === 'uptodate' ? `Up to date · v${APP_VERSION}`
        : phase === 'error' ? 'Check failed — tap to retry'
          : `Check for updates · v${APP_VERSION}`;

  return (
    <div className="rp-mobile-update">
      <button className="subtle" type="button" onClick={check} disabled={phase === 'checking'}>
        {label}
      </button>
    </div>
  );
}
