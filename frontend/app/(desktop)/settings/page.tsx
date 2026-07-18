'use client';

// Personal Settings hub. Everything here is a per-device preference
// (localStorage-backed, never PHI): theme, dictation engine behaviour, and
// links out to the dedicated Hotkeys and Sign-in & security screens.

import { useEffect, useState } from 'react';
import Link from 'next/link';
import {
  Sun,
  Moon,
  Monitor,
  Mic,
  Keyboard,
  ShieldCheck,
  ArrowRight,
  Sparkles,
  SpellCheck,
} from 'lucide-react';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import {
  getThemePreference,
  setThemePreference,
  THEME_CHANGE_EVENT,
  type ThemePreference,
} from '@/lib/theme';
import { STT_MODES, useSttMode } from '@/lib/dictation/sttMode';
import { useCrossCheckEnabled, useUseUbag } from '@/lib/dictation/crossCheckPrefs';
import type { SttMode } from '@/lib/api';

const THEME_OPTIONS: { value: ThemePreference; label: string; hint: string; icon: typeof Sun }[] = [
  { value: 'light', label: 'Light', hint: 'Bright clinical look', icon: Sun },
  { value: 'dark', label: 'Dark', hint: 'Deep navy, easy on the eyes', icon: Moon },
  { value: 'system', label: 'System', hint: 'Follow this device', icon: Monitor },
];

function sttModeLabel(m: SttMode): string {
  if (m === 'single') return 'Single (Parakeet)';
  if (m === 'ensemble') return 'Ensemble (cross-checked)';
  return 'Auto';
}

export default function SettingsPage() {
  // Theme preference — read after mount (prerender has no localStorage) and
  // stay in sync with the topbar ThemeToggle via the shared change event.
  const [themePref, setThemePref] = useState<ThemePreference>('light');
  useEffect(() => {
    setThemePref(getThemePreference());
    const onChange = () => setThemePref(getThemePreference());
    window.addEventListener(THEME_CHANGE_EVENT, onChange);
    return () => window.removeEventListener(THEME_CHANGE_EVENT, onChange);
  }, []);

  function chooseTheme(pref: ThemePreference) {
    setThemePref(pref);
    setThemePreference(pref);
  }

  const [sttMode, setSttMode] = useSttMode();
  const [crossCheck, setCrossCheck] = useCrossCheckEnabled();
  const [useUbag, setUseUbag] = useUseUbag();

  return (
    <Container>
      <PageHeader
        title="Settings"
        description="Personal preferences for this device — appearance, dictation, and shortcuts."
      />

      {/* ── Appearance ─────────────────────────────────────────────── */}
      <div className="rp-panel rp-anim-fade-in-up" style={{ marginBottom: 24 }}>
        <div className="rp-panel-title">Appearance</div>
        <p className="rp-page-sub" style={{ marginBottom: 12 }}>
          Choose how RadioPad looks on this device. System follows your OS setting.
        </p>
        <div role="radiogroup" aria-label="Theme" style={{ display: 'flex', gap: 10, flexWrap: 'wrap' }}>
          {THEME_OPTIONS.map(({ value, label, hint, icon: Icon }) => {
            const active = themePref === value;
            return (
              <label
                key={value}
                className={`rp-card${active ? ' bg-accent-soft' : ''}`}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 10,
                  padding: '12px 16px',
                  cursor: 'pointer',
                  minWidth: 180,
                }}
              >
                <input
                  type="radio"
                  name="theme"
                  value={value}
                  checked={active}
                  onChange={() => chooseTheme(value)}
                />
                <Icon size={16} strokeWidth={1.8} aria-hidden className={active ? 'text-accent' : 'text-ink-soft'} />
                <span>
                  <span style={{ display: 'block', fontWeight: 600 }}>{label}</span>
                  <span className="rp-page-sub">{hint}</span>
                </span>
              </label>
            );
          })}
        </div>
      </div>

      {/* ── Dictation ──────────────────────────────────────────────── */}
      <div className="rp-panel rp-anim-fade-in-up" style={{ marginBottom: 24 }}>
        <div className="rp-panel-title">
          <Mic size={15} strokeWidth={1.8} aria-hidden style={{ verticalAlign: -2, marginRight: 6 }} />
          Dictation
        </div>

        <div className="section-block" style={{ marginBottom: 16 }}>
          <label htmlFor="stt-mode">On-device engine mode</label>
          <select
            id="stt-mode"
            className="rp-input"
            style={{ maxWidth: 320 }}
            value={sttMode}
            onChange={(e) => setSttMode(e.target.value as SttMode)}
          >
            {STT_MODES.map((m) => (
              <option key={m} value={m}>
                {sttModeLabel(m)}
              </option>
            ))}
          </select>
          <p className="rp-page-sub" style={{ marginTop: 6 }}>
            Auto picks the best engine. Ensemble runs Parakeet and Windows Speech together and flags
            words they disagree on.
          </p>
        </div>

        <label style={{ display: 'flex', alignItems: 'flex-start', gap: 10, marginBottom: 12, cursor: 'pointer' }}>
          <input
            type="checkbox"
            checked={crossCheck}
            onChange={(e) => setCrossCheck(e.target.checked)}
            style={{ marginTop: 3 }}
          />
          <span>
            <span style={{ display: 'block', fontWeight: 600 }}>Show the Cross Check button</span>
            <span className="rp-page-sub">
              Keeps the session audio so you can re-run a dictation through the cross-check engines
              and verify the wording.
            </span>
          </span>
        </label>

        <label
          style={{
            display: 'flex',
            alignItems: 'flex-start',
            gap: 10,
            cursor: crossCheck ? 'pointer' : 'not-allowed',
            opacity: crossCheck ? 1 : 0.55,
          }}
        >
          <input
            type="checkbox"
            checked={useUbag}
            disabled={!crossCheck}
            onChange={(e) => setUseUbag(e.target.checked)}
            style={{ marginTop: 3 }}
          />
          <span>
            <span style={{ display: 'block', fontWeight: 600 }}>
              Route the medical review through UBAG{' '}
              <Sparkles size={13} strokeWidth={1.8} aria-hidden style={{ verticalAlign: -2 }} className="text-ai" />
            </span>
            <span className="rp-page-sub">
              Sends report text to a cloud AI service, so it stays off unless you opt in. You will be
              asked to confirm the report contains no patient-identifying information each time.
            </span>
          </span>
        </label>
      </div>

      {/* ── More settings ──────────────────────────────────────────── */}
      <div className="rp-grid-2 rp-anim-fade-in-up">
        <Link
          href="/settings/corrections"
          className="rp-card"
          style={{ display: 'flex', alignItems: 'center', gap: 12, textDecoration: 'none' }}
        >
          <SpellCheck size={18} strokeWidth={1.8} aria-hidden className="text-accent" />
          <span style={{ flex: 1 }}>
            <span style={{ display: 'block', fontWeight: 600 }}>Dictation corrections</span>
            <span className="rp-page-sub">Your personal spoken-word fixes, applied before formatting.</span>
          </span>
          <ArrowRight size={15} strokeWidth={1.8} aria-hidden className="text-ink-soft" />
        </Link>

        <Link
          href="/settings/hotkeys"
          className="rp-card"
          style={{ display: 'flex', alignItems: 'center', gap: 12, textDecoration: 'none' }}
        >
          <Keyboard size={18} strokeWidth={1.8} aria-hidden className="text-accent" />
          <span style={{ flex: 1 }}>
            <span style={{ display: 'block', fontWeight: 600 }}>Hotkeys</span>
            <span className="rp-page-sub">Review and customize keyboard shortcuts.</span>
          </span>
          <ArrowRight size={15} strokeWidth={1.8} aria-hidden className="text-ink-soft" />
        </Link>

        <Link
          href="/account/security"
          className="rp-card"
          style={{ display: 'flex', alignItems: 'center', gap: 12, textDecoration: 'none' }}
        >
          <ShieldCheck size={18} strokeWidth={1.8} aria-hidden className="text-accent" />
          <span style={{ flex: 1 }}>
            <span style={{ display: 'block', fontWeight: 600 }}>Sign-in &amp; security</span>
            <span className="rp-page-sub">Password, two-factor codes, and biometric sign-in.</span>
          </span>
          <ArrowRight size={15} strokeWidth={1.8} aria-hidden className="text-ink-soft" />
        </Link>
      </div>
    </Container>
  );
}
