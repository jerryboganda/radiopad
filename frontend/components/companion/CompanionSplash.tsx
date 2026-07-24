'use client';

/**
 * Brief branded loader shown once per app launch, before the pairing screen
 * mounts — the phone-app equivalent of a native splash screen (Capacitor
 * wraps this WebView with no launch-screen animation of its own). Purely
 * cosmetic: unmounts itself after a fixed dwell, no state or effect on the
 * pairing flow underneath.
 */

import { useEffect, useState } from 'react';

const DWELL_MS = 900;
const FADE_MS = 400;

export default function CompanionSplash() {
  const [mounted, setMounted] = useState(true);
  const [hidden, setHidden] = useState(false);

  useEffect(() => {
    const hideAt = setTimeout(() => setHidden(true), DWELL_MS);
    const unmountAt = setTimeout(() => setMounted(false), DWELL_MS + FADE_MS);
    return () => { clearTimeout(hideAt); clearTimeout(unmountAt); };
  }, []);

  if (!mounted) return null;

  return (
    <div className="rp-comp-splash" data-hidden={hidden} aria-hidden>
      <div className="rp-comp-splash-mark">
        <span className="rp-comp-splash-ring" />
        <span className="rp-comp-splash-ring" />
        <span className="rp-comp-splash-ring" />
        <span className="rp-comp-splash-badge">R</span>
      </div>
      <span className="rp-comp-splash-word">RadioPad</span>
      <span className="rp-comp-splash-tag">AI-assisted radiology reporting</span>
    </div>
  );
}
