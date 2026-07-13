/**
 * "Check for updates" for the mobile companion.
 *
 * The desktop self-updates through Tauri; a sideloaded Android APK can't, so the
 * companion checks the latest release and, if it's newer than the version baked
 * into this build, points the user at the APK to download + install.
 *
 * The check goes through the RadioPad backend (`GET /api/mobile/latest`), not
 * GitHub directly: the backend queries + caches the latest release server-side,
 * which avoids the GitHub API's 60/hr per-IP anonymous limit (a real risk behind a
 * shared hospital NAT) and reuses the CORS the mobile origin already has (raw
 * GitHub release-asset downloads send no CORS headers). The APK is attached to
 * each release as a stable-named asset by the `attach-android-release` workflow.
 */

import { companionBase } from './api';

/** Version this bundle was built from (baked by scripts/build-surface.mjs). */
export const APP_VERSION = (process.env.NEXT_PUBLIC_APP_VERSION || '0.0.0').trim();

export const RELEASES_URL = 'https://github.com/jerryboganda/radiopad/releases/latest';

export interface MobileUpdateInfo {
  current: string;
  latest: string;
  updateAvailable: boolean;
  /** Direct APK download URL when the release has the companion APK attached. */
  downloadUrl: string | null;
}

/** Compare dotted version strings. Returns true when `a` is strictly newer than `b`. */
export function isNewerVersion(a: string, b: string): boolean {
  const parse = (v: string) => v.replace(/^v/i, '').split('.').map((n) => parseInt(n, 10) || 0);
  const pa = parse(a);
  const pb = parse(b);
  for (let i = 0; i < Math.max(pa.length, pb.length); i += 1) {
    const x = pa[i] ?? 0;
    const y = pb[i] ?? 0;
    if (x !== y) return x > y;
  }
  return false;
}

interface LatestMobileResponse {
  version: string;
  apkUrl?: string | null;
  releaseUrl?: string;
}

/** Ask the backend for the latest release and derive whether an update is available. */
export async function checkMobileUpdate(current: string = APP_VERSION): Promise<MobileUpdateInfo> {
  const res = await fetch(`${companionBase()}/api/mobile/latest`, {
    headers: { Accept: 'application/json' },
    cache: 'no-store',
  });
  if (!res.ok) throw new Error(`Update check failed (${res.status})`);
  const data = (await res.json()) as LatestMobileResponse;
  const latest = (data.version || '0.0.0').replace(/^v/i, '');
  return {
    current,
    latest,
    updateAvailable: isNewerVersion(latest, current),
    downloadUrl: data.apkUrl ?? null,
  };
}
