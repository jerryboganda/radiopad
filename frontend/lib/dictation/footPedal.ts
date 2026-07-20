'use client';

// DESK-020 — foot-pedal support. Transcription foot pedals (Infinity, Olympus,
// Philips, VEC…) in keyboard mode emit ordinary key events (typically F13–F24
// or media keys), so pedal presses arrive as window `keydown`/`keyup` while the
// RadioPad window has focus. This module maps up to three pedals to dictation
// actions with true hold-to-talk semantics (down = start, up = stop) — which
// the chord-based hotkey registry (keydown-only) cannot express.
//
// True system-wide HID capture (pedal works while another app has focus) would
// need a Rust-side HID listener in the Tauri shell; keyboard-mode pedals are
// the documented, dependency-free path and cover the reporting workflow, where
// RadioPad is the focused app while dictating.
//
// Driving the mic: the floating dictation overlay owns start/stop and exposes
// a toggle event (`radiopad:dictate`) plus a listening broadcast
// (`radiopad:dictate-listening`). The pedal tracks the listening state from
// the broadcast and toggles via the event, so no overlay internals leak here.

import { useEffect } from 'react';
import { focusAdjacentSection } from '@/lib/editor/sectionEditorRegistry';

export interface FootPedalBindings {
  /** KeyboardEvent.code held to talk (down = start, up = stop). '' = unbound. */
  holdToTalk: string;
  /** KeyboardEvent.code that toggles hands-free dictation. '' = unbound. */
  toggleDictation: string;
  /** KeyboardEvent.code that focuses the next report section. '' = unbound. */
  nextField: string;
}

export const FOOT_PEDAL_ACTIONS: Array<{ key: keyof FootPedalBindings; label: string; description: string }> = [
  {
    key: 'holdToTalk',
    label: 'Hold to talk',
    description: 'Dictation runs only while this pedal is held down.',
  },
  {
    key: 'toggleDictation',
    label: 'Toggle dictation',
    description: 'Tap to start hands-free dictation, tap again to stop.',
  },
  {
    key: 'nextField',
    label: 'Next field',
    description: 'Moves the cursor to the next report section.',
  },
];

// Defaults match the F13/F14/F15 keys most pedals ship mapped to; harmless on
// keyboards without them.
export const DEFAULT_FOOT_PEDAL_BINDINGS: FootPedalBindings = {
  holdToTalk: 'F13',
  toggleDictation: 'F14',
  nextField: 'F15',
};

const KEY = 'radiopad:foot-pedal';
const EVENT = 'radiopad:foot-pedal-changed';
const DICTATE_EVENT = 'radiopad:dictate';
const DICTATE_STATE_EVENT = 'radiopad:dictate-listening';

function readStorage(): FootPedalBindings {
  try {
    const raw = window.localStorage.getItem(KEY);
    if (!raw) return { ...DEFAULT_FOOT_PEDAL_BINDINGS };
    const parsed = JSON.parse(raw) as Partial<FootPedalBindings>;
    return {
      holdToTalk: typeof parsed.holdToTalk === 'string' ? parsed.holdToTalk : DEFAULT_FOOT_PEDAL_BINDINGS.holdToTalk,
      toggleDictation: typeof parsed.toggleDictation === 'string' ? parsed.toggleDictation : DEFAULT_FOOT_PEDAL_BINDINGS.toggleDictation,
      nextField: typeof parsed.nextField === 'string' ? parsed.nextField : DEFAULT_FOOT_PEDAL_BINDINGS.nextField,
    };
  } catch {
    return { ...DEFAULT_FOOT_PEDAL_BINDINGS };
  }
}

let memBindings: FootPedalBindings =
  typeof window === 'undefined' ? { ...DEFAULT_FOOT_PEDAL_BINDINGS } : readStorage();

export function getFootPedalBindings(): FootPedalBindings {
  return { ...memBindings };
}

export function setFootPedalBinding(action: keyof FootPedalBindings, code: string): void {
  memBindings = { ...memBindings, [action]: code };
  if (typeof window === 'undefined') return;
  try {
    window.localStorage.setItem(KEY, JSON.stringify(memBindings));
  } catch {
    /* storage unavailable — in-memory bindings still apply */
  }
  window.dispatchEvent(new CustomEvent(EVENT));
}

export function resetFootPedalBindings(): void {
  memBindings = { ...DEFAULT_FOOT_PEDAL_BINDINGS };
  if (typeof window === 'undefined') return;
  try {
    window.localStorage.removeItem(KEY);
  } catch {
    /* noop */
  }
  window.dispatchEvent(new CustomEvent(EVENT));
}

export const FOOT_PEDAL_CHANGE_EVENT = EVENT;

/**
 * Attach the pedal listeners. Framework-agnostic; returns a detach function.
 * Exposed separately from the hook so the logic is unit-testable.
 */
export function attachFootPedal(win: Window = window): () => void {
  let listening = false;
  let heldStarted = false;

  const toggleMic = () => win.dispatchEvent(new CustomEvent(DICTATE_EVENT));

  const onState = (e: Event) => {
    listening = !!(e as CustomEvent<{ listening?: boolean }>).detail?.listening;
  };

  const onKeyDown = (e: KeyboardEvent) => {
    if (e.repeat) return;
    const b = memBindings;
    if (b.holdToTalk && e.code === b.holdToTalk) {
      e.preventDefault();
      if (!listening) {
        toggleMic();
        heldStarted = true;
      }
    } else if (b.toggleDictation && e.code === b.toggleDictation) {
      e.preventDefault();
      toggleMic();
    } else if (b.nextField && e.code === b.nextField) {
      e.preventDefault();
      focusAdjacentSection(1);
    }
  };

  const onKeyUp = (e: KeyboardEvent) => {
    const b = memBindings;
    if (b.holdToTalk && e.code === b.holdToTalk) {
      e.preventDefault();
      if (heldStarted && listening) toggleMic();
      heldStarted = false;
    }
  };

  win.addEventListener(DICTATE_STATE_EVENT, onState);
  win.addEventListener('keydown', onKeyDown);
  win.addEventListener('keyup', onKeyUp);
  return () => {
    win.removeEventListener(DICTATE_STATE_EVENT, onState);
    win.removeEventListener('keydown', onKeyDown);
    win.removeEventListener('keyup', onKeyUp);
  };
}

/** Mount once (the dictation overlay does) to enable foot-pedal control. */
export function useFootPedal(): void {
  useEffect(() => attachFootPedal(), []);
}
