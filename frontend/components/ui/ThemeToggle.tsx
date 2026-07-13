'use client';

/**
 * Light/dark theme switch (RC topbar + auth screens — THEME-002).
 * Renders the mockups' moon-icon + pill-switch control. Clicking flips
 * between explicit light/dark (an explicit choice overrides any tenant
 * or system default per the PRD theme precedence). The full
 * Light/Dark/System preference picker lives in Settings.
 */

import { useEffect, useState } from 'react';
import { Moon, Sun } from 'lucide-react';
import {
  applyTheme,
  resolveTheme,
  setThemePreference,
  watchSystemTheme,
  THEME_CHANGE_EVENT,
  type ResolvedTheme,
} from '@/lib/theme';

export default function ThemeToggle({ className }: { className?: string }) {
  // Render light on the server/first paint; sync to the real resolved
  // theme after mount (the bootstrap script has already themed the page,
  // so this only affects the control's own glyph).
  const [resolved, setResolved] = useState<ResolvedTheme>('light');

  useEffect(() => {
    setResolved(resolveTheme());
    applyTheme();
    const unwatch = watchSystemTheme();
    const onChange = () => setResolved(resolveTheme());
    window.addEventListener(THEME_CHANGE_EVENT, onChange);
    return () => {
      unwatch();
      window.removeEventListener(THEME_CHANGE_EVENT, onChange);
    };
  }, []);

  const dark = resolved === 'dark';

  return (
    <button
      type="button"
      role="switch"
      aria-checked={dark}
      aria-label={dark ? 'Switch to light theme' : 'Switch to dark theme'}
      title={dark ? 'Switch to light theme' : 'Switch to dark theme'}
      className={`rp-theme-toggle${className ? ` ${className}` : ''}`}
      onClick={() => setResolved(setThemePreference(dark ? 'light' : 'dark'))}
    >
      {dark ? <Moon size={15} aria-hidden /> : <Sun size={15} aria-hidden />}
      <span className="rp-theme-toggle-track" aria-hidden>
        <span className="rp-theme-toggle-thumb" />
      </span>
    </button>
  );
}
