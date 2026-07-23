'use client';

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { startAutoSync } from '@/lib/offlineDrafts';
import { getAuthToken } from '@/lib/secureAuth';
import { setActiveAuthToken } from '@/lib/api';
import { isBiometricLockEnabled, unlockWithBiometric } from '@/lib/biometric';
import { installDictationHotkey } from '@/lib/dictationHotkey';
import { installDesktopHotkeySync } from '@/lib/desktopHotkeys';

type TauriUnlisten = () => void;
type TauriListenHandle = TauriUnlisten | { unlisten?: TauriUnlisten } | null | undefined;

type TauriGlobal = {
  event?: { listen?: (event: string, handler: (event?: { payload?: unknown }) => void) => Promise<TauriListenHandle> | TauriListenHandle };
  listen?: (event: string, handler: (event?: { payload?: unknown }) => void) => Promise<TauriListenHandle> | TauriListenHandle;
};

function getTauri(): TauriGlobal | null {
  if (typeof window === 'undefined') return null;
  return (window as typeof window & { __TAURI__?: TauriGlobal }).__TAURI__ ?? null;
}

// ---------------------------------------------------------------------------
// Native OS notifications for finished AI jobs (async-jobs Phase 7)
//
// `JobsProvider` dispatches `radiopad:job-terminal` exactly once per job on
// every terminal transition. When that fires while the desktop window is
// unfocused/minimised, raise a native Windows toast so the radiologist knows a
// generation finished without watching the app. Body is PHI-minimised
// (NOTIF-004): modality/bodyPart only — never accession, name, or MRN.
// ---------------------------------------------------------------------------

type JobTerminalDetail = {
  jobId?: string;
  status?: 'ok' | 'error' | 'cancelled';
  kind?: string;
  mode?: string;
  reportId?: string;
  // PHI-minimised by construction (JobsProvider strips accession/identifiers).
  report?: { modality?: string; bodyPart?: string } | null;
};

/**
 * The app is "away" — so a finished job warrants an OS toast — whenever its
 * window is not the active foreground window. `document.hidden` covers
 * minimised / occluded; `!document.hasFocus()` covers the visible-but-blurred
 * case (another app in front). Both are standard DOM APIs, so no extra Tauri
 * import or capability is needed just to read focus state.
 */
function appIsAway(): boolean {
  if (typeof document === 'undefined') return false;
  if (document.hidden) return true;
  return typeof document.hasFocus === 'function' ? !document.hasFocus() : false;
}

/** PHI-safe toast title from the terminal status + job kind/mode. */
function jobToastTitle(detail: JobTerminalDetail): string {
  if (detail.status === 'error') return 'Generation failed';
  if (detail.status === 'cancelled') return 'Generation cancelled';
  // status === 'ok'
  if (detail.kind === 'ai') {
    if (detail.mode === 'impression') return 'Impression ready';
    if (detail.mode === 'rewrite') return 'Rewrite ready';
    return 'AI result ready';
  }
  return 'Draft ready'; // generate / local-generate
}

/**
 * PHI-minimised toast body: modality + body-part only (e.g. "CT Chest"). Never
 * the accession, patient name, MRN, or any identifier — this string sits in the
 * system tray, visible to anyone glancing at the screen (NOTIF-004). Empty when
 * no study descriptor is present, in which case only the title is shown.
 */
function jobToastBody(detail: JobTerminalDetail): string {
  const r = detail.report;
  if (!r) return '';
  return [r.modality, r.bodyPart]
    .map((s) => (typeof s === 'string' ? s.trim() : ''))
    .filter((s) => s.length > 0)
    .join(' ');
}

// ---------------------------------------------------------------------------
// Native OS notifications for in-app notifications (NOTIF-001, PR-N4)
//
// Mirrors the job-terminal handler. PR-N5's NotificationsProvider will dispatch
// `radiopad:notification` once per new notification SSE event; until then this
// listener is inert (dead code), exactly as the job-terminal handler was built
// incrementally. Body is PHI-minimised (NOTIF-004): a generic category phrase as
// the title, and at most modality/bodyPart as the body — never findings,
// accession, name, or MRN.
// ---------------------------------------------------------------------------

type NotificationDetail = {
  id?: string;
  category?: string;
  urgency?: 'Info' | 'Warning' | 'Critical';
  title?: string;
  linkHref?: string;
  // PHI-minimised by construction if the dispatcher attaches a study descriptor.
  report?: { modality?: string; bodyPart?: string } | null;
};

/** PHI-safe generic category phrase for the toast title (never the raw row Title). */
function notificationToastTitle(detail: NotificationDetail): string {
  switch (detail.category) {
    case 'CriticalResult':
      return 'Critical result';
    case 'PeerReview':
      return 'Peer review';
    case 'RulebookApproval':
      return 'Rulebook update';
    case 'TemplateApproval':
      return 'Template review';
    case 'AiJob':
      return 'AI report';
    case 'System':
      return 'RadioPad alert';
    default:
      return 'RadioPad';
  }
}

/**
 * PHI-minimised toast body (NOTIF-004): modality + body-part when the event
 * carries a study descriptor, otherwise the notification's already-in-app-safe
 * Title. Never the finding summary, accession, patient name, or MRN.
 */
function notificationToastBody(detail: NotificationDetail): string {
  const r = detail.report;
  const study = r
    ? [r.modality, r.bodyPart]
        .map((s) => (typeof s === 'string' ? s.trim() : ''))
        .filter((s) => s.length > 0)
        .join(' ')
    : '';
  if (study) return study;
  return typeof detail.title === 'string' ? detail.title.trim() : '';
}

/**
 * Fire a native OS notification for a finished AI job when the app window is
 * unfocused/minimised. Best-effort throughout: guarded to the desktop (Tauri)
 * shell, checks/requests permission, and fails silently (logs, never throws) so
 * it can never block ShellBridge's other side effects.
 *
 * Desktop click-through is a documented platform LIMITATION, not a deferral:
 * tauri-plugin-notification v2's Actions/click-activation API is mobile-only
 * (iOS/Android) — Windows desktop exposes no click callback, so clicking a toast
 * only performs the OS-default window activation. Verified against
 * v2.tauri.app/plugin/notification (2026-07-23). Future seam: were the plugin to
 * ship desktop activation (or we adopt a WinRT-toast fork), the handler is
 * `navigate to notification.LinkHref` (the `?aiJob=` deep-link machinery already
 * exists). Meanwhile, on window focus / visibilitychange→visible after any toast
 * was raised, PR-N5's NotificationsProvider refreshes the unread-count so the
 * bell badge is accurate the instant the user arrives — the LinkHref target is
 * then one bell-click away.
 */
async function notifyJobTerminal(detail: JobTerminalDetail): Promise<void> {
  // Defense in depth: JobsProvider is desktop-gated, but this is a plain browser
  // event that could be dispatched on any surface. Only the desktop shell has
  // the notification plugin registered.
  if (typeof window === 'undefined' || !('__TAURI__' in window)) return;
  // Focused → the in-app toast + jobs widget already surface the result; a
  // second OS toast would be noise.
  if (!appIsAway()) return;
  try {
    const { isPermissionGranted, requestPermission, sendNotification } = await import(
      '@tauri-apps/plugin-notification'
    );
    let granted = await isPermissionGranted();
    if (!granted) granted = (await requestPermission()) === 'granted';
    if (!granted) return; // permission denied — degrade silently
    const title = jobToastTitle(detail);
    const body = jobToastBody(detail);
    sendNotification(body ? { title, body } : { title });
  } catch (err) {
    // Not under Tauri, plugin missing, or the OS refused — never throw into the
    // caller; the rest of ShellBridge's side effects must keep running.
    console.warn('[shell-bridge] OS notification failed', err);
  }
}

/**
 * Fire a native OS notification for a new in-app notification when the app window
 * is unfocused/minimised. Same desktop-gating, away-gate, permission flow, and
 * PHI-minimisation doctrine as {@link notifyJobTerminal}. Dispatched by PR-N5's
 * NotificationsProvider (dead code until then, by design).
 */
async function notifyNotification(detail: NotificationDetail): Promise<void> {
  if (typeof window === 'undefined' || !('__TAURI__' in window)) return;
  if (!appIsAway()) return;
  try {
    const { isPermissionGranted, requestPermission, sendNotification } = await import(
      '@tauri-apps/plugin-notification'
    );
    let granted = await isPermissionGranted();
    if (!granted) granted = (await requestPermission()) === 'granted';
    if (!granted) return; // permission denied — degrade silently
    const title = notificationToastTitle(detail);
    const body = notificationToastBody(detail);
    sendNotification(body ? { title, body } : { title });
  } catch (err) {
    console.warn('[shell-bridge] OS notification failed', err);
  }
}

/**
 * Bridges native shells (Tauri desktop, Capacitor mobile) to the Next.js app.
 *
 * Desktop (Tauri): listens for `radiopad://new-report` emitted by the global
 * `Ctrl/Cmd+Shift+N` shortcut and routes to a fresh report editor.
 *
 * Mobile (Capacitor): registers a network listener that flushes the offline
 * draft queue whenever connectivity returns.
 *
 * Both bindings are best-effort. When the runtime is a regular browser the
 * dynamic imports fail silently and this component is a no-op.
 */
export default function ShellBridge() {
  const router = useRouter();

  useEffect(() => {
    let unsub: Array<() => void> = [];
    let cancelled = false;

    // Async-jobs Phase 7 — native OS notification when an AI generation job
    // finishes while the window is unfocused/minimised. `JobsProvider` emits
    // this event (desktop surface only); the handler self-guards to Tauri and to
    // the away state, so it is inert everywhere else.
    const onJobTerminal = (event: Event) => {
      const detail = (event as CustomEvent<JobTerminalDetail>).detail;
      if (detail) void notifyJobTerminal(detail);
    };
    window.addEventListener('radiopad:job-terminal', onJobTerminal);

    // NOTIF-001 (PR-N4) — OS toast for a new in-app notification. PR-N5's
    // NotificationsProvider will dispatch `radiopad:notification`; inert until then.
    const onNotification = (event: Event) => {
      const detail = (event as CustomEvent<NotificationDetail>).detail;
      if (detail) void notifyNotification(detail);
    };
    window.addEventListener('radiopad:notification', onNotification);

    (async () => {
      try {
        const tauri = getTauri();
        const listen = tauri?.event?.listen ?? tauri?.listen;
        if (!listen || cancelled) return;
        // PRD DESK-003 — global hotkeys are registered Rust-side in
        // `desktop/src-tauri/src/main.rs`. The shell forwards the user
        // intent as `radiopad://*` events; this bridge translates each
        // event into a navigation or DOM-level action.
        const dispatchAction = (name: string) => {
          if (typeof window === 'undefined') return;
          window.dispatchEvent(new CustomEvent(`radiopad:${name}`));
        };
        const dispatchPayload = (name: string, payload: unknown) => {
          if (typeof window === 'undefined') return;
          window.dispatchEvent(new CustomEvent(`radiopad:${name}`, { detail: payload }));
        };
        const reg = async (event: string, handler: (event?: { payload?: unknown }) => void) => {
          const handle = await listen(event, handler);
          const u = typeof handle === 'function'
            ? handle
            : handle?.unlisten;
          if (typeof u === 'function') unsub.push(u);
        };
        await reg('radiopad://new-report', () => router.push('/?new=1'));
        await reg('radiopad://generate-impression', () =>
          dispatchAction('generate-impression'),
        );
        await reg('radiopad://rewrite', () => dispatchAction('rewrite'));
        await reg('radiopad://dictate', () => dispatchAction('dictate'));
        await reg('radiopad://secure-copy-section', () =>
          dispatchAction('secure-copy-section'),
        );
        await reg('radiopad://clipboard-cleared', () =>
          dispatchAction('clipboard-cleared'),
        );
        await reg('radiopad://backend-status', (event) =>
          dispatchPayload('backend-status', event?.payload ?? null),
        );
      } catch {
        /* not running under Tauri — no-op */
      }
    })();
    // P0.3 — in-app rebindable dictation hotkey. Works on every surface (including web, which has
    // no Rust global shortcut) and honours the user's configured chord. The desktop Rust global
    // shortcut still covers the system-wide (unfocused) case; a chord the OS has claimed as a
    // global shortcut never reaches this listener, so there is no double-fire.
    const uninstallDictationHotkey = installDictationHotkey();
    // Keep the OS-level global shortcuts in step with the user's rebindings (desktop only).
    const uninstallDesktopHotkeySync = installDesktopHotkeySync();

    // Mobile/desktop offline-draft auto sync.
    startAutoSync().catch(() => { /* best effort */ });
    // Hydrate the bearer from native secure storage (Tauri keyring on desktop,
    // Keychain / Keystore on mobile; Preferences/localStorage in dev preview).
    // PRD MOB-008 — when the user has enabled biometric lock, gate the token
    // release on a Face ID / Touch ID / Android biometric prompt.
    (async () => {
      try {
        if (await isBiometricLockEnabled()) {
          const ok = await unlockWithBiometric('Unlock RadioPad to continue');
          if (!ok) { setActiveAuthToken(null); return; }
        }
        const tok = await getAuthToken();
        if (!cancelled) setActiveAuthToken(tok ?? null);
      } catch {
        /* best effort */
      }
    })();
    return () => {
      cancelled = true;
      window.removeEventListener('radiopad:job-terminal', onJobTerminal);
      window.removeEventListener('radiopad:notification', onNotification);
      uninstallDictationHotkey();
      uninstallDesktopHotkeySync();
      for (const u of unsub) {
        try { u(); } catch { /* ignore */ }
      }
      unsub = [];
    };
  }, [router]);

  return null;
}
