/**
 * PRD MOB-007 — mobile push registration helpers.
 *
 * Wraps `@capacitor/push-notifications` so that pages don't import the
 * Capacitor plugin directly. On the web (and inside the Next.js dev preview)
 * the plugin import fails silently and the helpers degrade to no-ops; that
 * keeps the desktop / browser builds free of native warnings.
 *
 * Notification payloads MUST stay PHI-free; the backend enforces this on the
 * sender side. Routing data lives in `data.kind` + `data.entityId`.
 */

import { api } from './api';
import { getNativeCapacitorPlatform } from './nativeRuntime';

export type PushPlatform = 'ios' | 'android' | 'web';

export type PushRegistration = { token: string; platform: PushPlatform };

function detectPlatform(): PushPlatform {
  return getNativeCapacitorPlatform() ?? 'web';
}

/**
 * Requests permission, registers with APNs/FCM, persists the token on the
 * backend, and returns the registration. Returns null on web where push is
 * out of scope for v0.1.
 */
export async function registerForPush(): Promise<PushRegistration | null> {
  const platform = detectPlatform();
  if (platform === 'web') return null;

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const mod: any = await import('@capacitor/push-notifications').catch(() => null);
  const PushNotifications = mod?.PushNotifications;
  if (!PushNotifications) return null;

  const perm = await PushNotifications.requestPermissions();
  if (perm?.receive !== 'granted') return null;

  const token = await new Promise<string | null>((resolve, reject) => {
    let settled = false;
    const settle = (fn: () => void) => { if (!settled) { settled = true; fn(); } };

    PushNotifications.addListener('registration', (t: { value: string }) => {
      settle(() => resolve(t?.value ?? null));
    });
    PushNotifications.addListener('registrationError', (err: unknown) => {
      settle(() => reject(err));
    });
    PushNotifications.register().catch((err: unknown) => settle(() => reject(err)));
  });

  if (!token) return null;
  await api.push.registerDevice(token, platform);
  return { token, platform };
}

/**
 * Best-effort unregister: instructs the backend to drop the row for the
 * supplied token. The native plugin keeps the token at the OS level; uninstall
 * is the only true revoke.
 */
export async function unregisterFromPush(token: string): Promise<void> {
  if (!token) return;
  try {
    await api.push.unregisterDevice(token);
  } catch {
    /* best effort */
  }
}
