/**
 * PRD MOB-008 — biometric unlock for the cached bearer token. Wraps
 * `@aparajita/capacitor-biometric-auth` so pages don't import the plugin
 * directly. On web, all helpers degrade to no-ops; native shells gate the
 * release of the secure-store token on Face ID / Touch ID / Android biometric
 * prompt success.
 */

import { setActiveAuthToken } from './api';
import { getAuthToken } from './secureAuth';

const BIOMETRIC_PREF_KEY = 'radiopad.biometricLock';

// eslint-disable-next-line @typescript-eslint/no-explicit-any
async function loadBiometric(): Promise<any | null> {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const mod: any = await import('@aparajita/capacitor-biometric-auth').catch(() => null);
  return mod?.BiometricAuth ?? null;
}

// eslint-disable-next-line @typescript-eslint/no-explicit-any
async function loadPreferences(): Promise<any | null> {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const mod: any = await import('@capacitor/preferences').catch(() => null);
  return mod?.Preferences ?? null;
}

export async function isBiometricAvailable(): Promise<boolean> {
  const bio = await loadBiometric();
  if (!bio) return false;
  try {
    const res = await bio.checkBiometry();
    return Boolean(res?.isAvailable);
  } catch {
    return false;
  }
}

/**
 * Reads the persisted "biometric lock enabled" flag. Defaults to false in
 * dev/web where the plugin is unavailable.
 */
export async function isBiometricLockEnabled(): Promise<boolean> {
  const prefs = await loadPreferences();
  if (!prefs) return false;
  try {
    const { value } = await prefs.get({ key: BIOMETRIC_PREF_KEY });
    return value === '1';
  } catch {
    return false;
  }
}

export async function enableBiometricLock(enabled: boolean): Promise<void> {
  const prefs = await loadPreferences();
  if (!prefs) return;
  try {
    if (enabled) await prefs.set({ key: BIOMETRIC_PREF_KEY, value: '1' });
    else await prefs.remove({ key: BIOMETRIC_PREF_KEY });
  } catch {
    /* best effort */
  }
}

/**
 * Prompts the OS biometric sheet. Resolves true when the user authenticates,
 * false otherwise. Web returns true so the dev preview remains usable.
 */
export async function unlockWithBiometric(reason: string): Promise<boolean> {
  const bio = await loadBiometric();
  if (!bio) return true;
  try {
    await bio.authenticate({
      reason,
      cancelTitle: 'Cancel',
      allowDeviceCredential: true,
      iosFallbackTitle: 'Use device passcode',
      androidTitle: 'RadioPad',
      androidSubtitle: reason,
    });
    return true;
  } catch {
    return false;
  }
}

/**
 * App-launch hook: when biometric lock is enabled, prompt before exposing the
 * cached auth token to the API client. Call once at startup (e.g. from
 * ShellBridge) — the API client stays anonymous until this resolves.
 */
export async function gateAuthTokenWithBiometric(): Promise<void> {
  const enabled = await isBiometricLockEnabled();
  if (!enabled) return;
  const ok = await unlockWithBiometric('Unlock RadioPad to continue');
  if (!ok) {
    setActiveAuthToken(null);
    return;
  }
  try {
    const tok = await getAuthToken();
    setActiveAuthToken(tok ?? null);
  } catch {
    /* best effort */
  }
}
