/**
 * RC theme runtime (PRD v3.0 THEME-001..006).
 *
 * Preference model: 'light' | 'dark' | 'system'. Light is the first-run
 * default on every surface (THEME-001). The preference is persisted
 * per-device in localStorage under `rp-theme` (THEME-004 — device-local,
 * never PHI). The resolved theme is applied as `data-theme` on <html>;
 * an inline bootstrap script in app/layout.tsx applies it before first
 * paint (THEME-006), and this module owns every later change so a switch
 * never reloads or re-mounts anything (THEME-005).
 */

export type ThemePreference = 'light' | 'dark' | 'system';
export type ResolvedTheme = 'light' | 'dark';

export const THEME_STORAGE_KEY = 'rp-theme';
export const THEME_CHANGE_EVENT = 'rp-theme-change';

/** Browser-chrome color per resolved theme (meta[name=theme-color]). */
export const THEME_COLORS: Record<ResolvedTheme, string> = {
  light: '#f5f8fb',
  dark: '#0b1422',
};

const isBrowser = () => typeof window !== 'undefined';

export function getThemePreference(): ThemePreference {
  if (!isBrowser()) return 'light';
  try {
    const raw = window.localStorage.getItem(THEME_STORAGE_KEY);
    if (raw === 'light' || raw === 'dark' || raw === 'system') return raw;
  } catch {
    /* storage unavailable (private mode etc.) — fall through to default */
  }
  return 'light';
}

export function resolveTheme(pref: ThemePreference = getThemePreference()): ResolvedTheme {
  if (pref === 'system') {
    if (isBrowser() && window.matchMedia('(prefers-color-scheme: dark)').matches) return 'dark';
    return 'light';
  }
  return pref;
}

/** Apply the resolved theme to the document (html[data-theme] + meta theme-color). */
export function applyTheme(pref: ThemePreference = getThemePreference()): ResolvedTheme {
  const resolved = resolveTheme(pref);
  if (!isBrowser()) return resolved;
  const root = document.documentElement;
  if (resolved === 'dark') root.setAttribute('data-theme', 'dark');
  else root.removeAttribute('data-theme');
  const meta = document.querySelector('meta[name="theme-color"]');
  if (meta) meta.setAttribute('content', THEME_COLORS[resolved]);
  return resolved;
}

export function setThemePreference(pref: ThemePreference): ResolvedTheme {
  try {
    window.localStorage.setItem(THEME_STORAGE_KEY, pref);
  } catch {
    /* non-fatal: theme still applies for this session */
  }
  const resolved = applyTheme(pref);
  window.dispatchEvent(
    new CustomEvent(THEME_CHANGE_EVENT, { detail: { preference: pref, resolved } }),
  );
  return resolved;
}

/**
 * Keep the document in sync with OS scheme changes while the preference
 * is 'system'. Returns an unsubscribe function.
 */
export function watchSystemTheme(): () => void {
  if (!isBrowser()) return () => {};
  const mql = window.matchMedia('(prefers-color-scheme: dark)');
  const onChange = () => {
    if (getThemePreference() === 'system') applyTheme('system');
  };
  mql.addEventListener('change', onChange);
  return () => mql.removeEventListener('change', onChange);
}

/**
 * Inline pre-paint bootstrap (stringified into a <script> tag in
 * app/layout.tsx). Must stay dependency-free and mirror the logic above.
 */
export const THEME_BOOTSTRAP_SCRIPT = `(function(){try{var p=localStorage.getItem('${THEME_STORAGE_KEY}')||'light';var d=p==='dark'||(p==='system'&&matchMedia('(prefers-color-scheme: dark)').matches);if(d)document.documentElement.setAttribute('data-theme','dark');var m=document.querySelector('meta[name="theme-color"]');if(m)m.setAttribute('content',d?'${THEME_COLORS.dark}':'${THEME_COLORS.light}');}catch(e){}})();`;
