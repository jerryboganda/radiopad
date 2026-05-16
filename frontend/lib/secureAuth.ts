/**
 * PRD DESK-008 / MOB-006 — secure store for the bearer/session token used by
 * the API client when running inside the Tauri desktop or Capacitor mobile shell.
 *
 * Uses the Tauri keyring bridge on desktop, `capacitor-secure-storage-plugin`
 * when available on mobile (Keychain on iOS, Keystore-backed
 * EncryptedSharedPreferences on Android), then `@capacitor/preferences`, and
 * finally `localStorage` for the web preview.
 *
 * The web fallback is **not** secure storage — it is only for the dev
 * preview. Production desktop/mobile builds must use the native secure paths.
 */

const KEY = 'radiopad.auth.token.v1';

type Backend = {
  get(): Promise<string | null>;
  set(value: string): Promise<void>;
  clear(): Promise<void>;
  isSecure: boolean;
};

let backend: Backend | null = null;

function tauriInvoke(): undefined | ((cmd: string, args?: unknown) => Promise<unknown>) {
  if (typeof window === 'undefined') return undefined;
  const tauri = (window as typeof window & {
    __TAURI__?: {
      core?: { invoke?: (cmd: string, args?: unknown) => Promise<unknown> };
      invoke?: (cmd: string, args?: unknown) => Promise<unknown>;
    };
  }).__TAURI__;
  return tauri?.core?.invoke ?? tauri?.invoke;
}

async function pickBackend(): Promise<Backend> {
  if (backend) return backend;

  // 1. Desktop secure storage (Windows Credential Manager / macOS Keychain /
  // Linux Secret Service), exposed through narrow Tauri commands.
  const invoke = tauriInvoke();
  if (invoke) {
    backend = {
      async get() {
        const value = await invoke('device_pairing_token_get');
        return typeof value === 'string' && value.length > 0 ? value : null;
      },
      async set(value: string) {
        await invoke('device_pairing_token_set', { token: value });
      },
      async clear() {
        await invoke('device_pairing_token_clear');
      },
      isSecure: true,
    };
    return backend;
  }

  // 2. Native mobile secure storage (iOS Keychain / Android Keystore).
  try {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const sec: any = await import('capacitor-secure-storage-plugin').catch(() => null);
    if (sec?.SecureStoragePlugin) {
      backend = {
        async get() {
          try {
            const v = await sec.SecureStoragePlugin.get({ key: KEY });
            return (v?.value as string | undefined) ?? null;
          } catch { return null; }
        },
        async set(value: string) {
          await sec.SecureStoragePlugin.set({ key: KEY, value });
        },
        async clear() {
          try { await sec.SecureStoragePlugin.remove({ key: KEY }); } catch { /* ignore */ }
        },
        isSecure: true,
      };
      return backend;
    }
  } catch { /* fall through */ }

  // 3. Capacitor Preferences (encrypted-at-rest on iOS, plain on Android).
  try {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const mod: any = await import('@capacitor/preferences').catch(() => null);
    if (mod?.Preferences) {
      backend = {
        async get() {
          const { value } = await mod.Preferences.get({ key: KEY });
          return (value as string | null) ?? null;
        },
        async set(value: string) {
          await mod.Preferences.set({ key: KEY, value });
        },
        async clear() {
          await mod.Preferences.remove({ key: KEY });
        },
        isSecure: false,
      };
      return backend;
    }
  } catch { /* fall through */ }

  // 4. Last-resort web fallback. Not for production.
  backend = {
    async get() {
      if (typeof localStorage === 'undefined') return null;
      return localStorage.getItem(KEY);
    },
    async set(value: string) {
      if (typeof localStorage === 'undefined') return;
      localStorage.setItem(KEY, value);
    },
    async clear() {
      if (typeof localStorage === 'undefined') return;
      localStorage.removeItem(KEY);
    },
    isSecure: false,
  };
  return backend;
}

export async function getAuthToken(): Promise<string | null> {
  return (await pickBackend()).get();
}

export async function setAuthToken(token: string): Promise<void> {
  await (await pickBackend()).set(token);
}

export async function clearAuthToken(): Promise<void> {
  await (await pickBackend()).clear();
}

/** Whether the token is currently held in OS-level secure storage. */
export async function isAuthTokenSecure(): Promise<boolean> {
  return (await pickBackend()).isSecure;
}
