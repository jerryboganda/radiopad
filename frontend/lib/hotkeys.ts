// Hotkey registry (RC-10 — Hotkey customization).
//
// Single source of truth for every keyboard shortcut RadioPad ships, plus a
// small persistence layer for per-device custom bindings. Only shortcuts that
// something in the app actually implements are marked `implemented: true`:
//
//  - Ctrl/Cmd+K       → command palette (components/shell/Topbar.tsx keydown)
//  - Ctrl/Cmd+Shift+D → dictation toggle (desktop global shortcut in
//    desktop/src-tauri/src/main.rs → `radiopad://dictate` → the
//    `radiopad:dictate` event that DictationOverlay owns)
//  - Ctrl/Cmd+Shift+N/I/W/C/R → the remaining desktop global shortcuts
//    registered in main.rs (new report, generate impression, rewrite picker,
//    secure-copy section, focus window)
//
// Aspirational shortcuts carry `implemented: false` and render under a
// "Coming soon" group — they must never be presented as live.
//
// Custom bindings are stored per-device in localStorage under `rp-hotkeys`
// (id → binding). Like the theme preference, this is device-local and never
// PHI. A `rp-hotkeys-change` CustomEvent fires after every write so any open
// view can stay in sync.

export type HotkeyCategory =
  | 'Report Composer'
  | 'Dictation'
  | 'Navigation'
  | 'AI Actions'
  | 'Global';

export type HotkeyScope = 'global' | 'editor';

export interface HotkeyDef {
  id: string;
  /** Short action label ("Open command palette"). */
  label: string;
  /** One-line description of what the shortcut does. */
  description?: string;
  category: HotkeyCategory;
  /** Canonical default chord, e.g. 'Ctrl+Shift+D'. */
  defaultBinding: string;
  /** Where the shortcut applies: everywhere, or inside the report editor. */
  scope: HotkeyScope;
  /** False = planned but not wired up anywhere yet ("Coming soon"). */
  implemented: boolean;
}

export const HOTKEY_STORAGE_KEY = 'rp-hotkeys';
export const HOTKEYS_CHANGE_EVENT = 'rp-hotkeys-change';

/** Display / grouping order for categories. */
export const HOTKEY_CATEGORIES: HotkeyCategory[] = [
  'Report Composer',
  'Dictation',
  'Navigation',
  'AI Actions',
  'Global',
];

export const HOTKEYS: HotkeyDef[] = [
  // ── Live shortcuts (each one verified against real code) ──────────────
  {
    id: 'command-palette',
    label: 'Open command palette',
    description: 'Search and jump anywhere from the topbar palette.',
    category: 'Global',
    defaultBinding: 'Ctrl+K',
    scope: 'global',
    implemented: true,
  },
  {
    id: 'dictation-toggle',
    label: 'Start or stop dictation',
    description: 'Toggles the floating dictation mic on and off.',
    category: 'Dictation',
    defaultBinding: 'Ctrl+Shift+D',
    scope: 'global',
    implemented: true,
  },
  {
    id: 'new-report',
    label: 'Start a new report',
    description: 'Opens the new-report wizard from anywhere.',
    category: 'Navigation',
    defaultBinding: 'Ctrl+Shift+N',
    scope: 'global',
    implemented: true,
  },
  {
    id: 'focus-window',
    label: 'Focus the RadioPad window',
    description: 'Brings the desktop app to the front (works system-wide).',
    category: 'Global',
    defaultBinding: 'Ctrl+Shift+R',
    scope: 'global',
    implemented: true,
  },
  {
    id: 'generate-impression',
    label: 'Generate impression',
    description: 'Asks the AI to draft the impression for the open report.',
    category: 'AI Actions',
    defaultBinding: 'Ctrl+Shift+I',
    scope: 'editor',
    implemented: true,
  },
  {
    id: 'rewrite-mode',
    label: 'Open the rewrite picker',
    description: 'Choose a rewrite style for the focused report text.',
    category: 'AI Actions',
    defaultBinding: 'Ctrl+Shift+W',
    scope: 'editor',
    implemented: true,
  },
  {
    id: 'secure-copy-section',
    label: 'Copy the focused section',
    description: 'Secure-copies the focused report section to the clipboard.',
    category: 'Report Composer',
    defaultBinding: 'Ctrl+Shift+C',
    scope: 'editor',
    implemented: true,
  },

  // ── Coming soon (nothing implements these yet) ─────────────────────────
  {
    id: 'validate-report',
    label: 'Run validation',
    description: 'Run all rulebook validations on the open report.',
    category: 'Report Composer',
    defaultBinding: 'Ctrl+Shift+V',
    scope: 'editor',
    implemented: false,
  },
  {
    id: 'insert-template',
    label: 'Insert template',
    description: 'Insert the resolved template into the report body.',
    category: 'Report Composer',
    defaultBinding: 'Ctrl+Shift+T',
    scope: 'editor',
    implemented: false,
  },
  {
    id: 'next-section',
    label: 'Jump to next section',
    description: 'Move the caret to the next report section.',
    category: 'Navigation',
    defaultBinding: 'Ctrl+]',
    scope: 'editor',
    implemented: false,
  },
  {
    id: 'previous-section',
    label: 'Jump to previous section',
    description: 'Move the caret to the previous report section.',
    category: 'Navigation',
    defaultBinding: 'Ctrl+[',
    scope: 'editor',
    implemented: false,
  },
  {
    id: 'toggle-theme',
    label: 'Toggle light / dark theme',
    description: 'Switch between the light and dark themes.',
    category: 'Global',
    defaultBinding: 'Ctrl+Shift+L',
    scope: 'global',
    implemented: false,
  },
];

const MODIFIER_ORDER = ['Ctrl', 'Alt', 'Shift', 'Meta'] as const;
type Modifier = (typeof MODIFIER_ORDER)[number];

const MODIFIER_ALIASES: Record<string, Modifier> = {
  ctrl: 'Ctrl',
  control: 'Ctrl',
  alt: 'Alt',
  option: 'Alt',
  shift: 'Shift',
  meta: 'Meta',
  cmd: 'Meta',
  command: 'Meta',
  win: 'Meta',
  super: 'Meta',
};

const KEY_ALIASES: Record<string, string> = {
  ' ': 'Space',
  space: 'Space',
  spacebar: 'Space',
  escape: 'Esc',
  esc: 'Esc',
  arrowup: 'Up',
  arrowdown: 'Down',
  arrowleft: 'Left',
  arrowright: 'Right',
  return: 'Enter',
  enter: 'Enter',
  backquote: '`',
  plus: '+',
};

function normalizeKeyToken(raw: string): string {
  const trimmed = raw.trim();
  if (!trimmed) return '';
  const alias = KEY_ALIASES[trimmed.toLowerCase()];
  if (alias) return alias;
  if (trimmed.length === 1) return trimmed.toUpperCase();
  // Multi-char keys (F1..F12, Tab, Delete, …): Title-case the first letter.
  return trimmed.charAt(0).toUpperCase() + trimmed.slice(1);
}

/**
 * Canonicalize a binding string: modifiers sorted Ctrl → Alt → Shift → Meta,
 * key token last, joined with '+'. `'shift + ctrl + d'` → `'Ctrl+Shift+D'`.
 * Returns '' for empty / modifier-only input.
 */
export function normalizeBinding(binding: string): string {
  const parts = binding.split('+').map((p) => p.trim()).filter(Boolean);
  // A trailing literal '+' key ("Ctrl++") survives as an empty tail part —
  // splitting already dropped it, so a lone '+' binding is handled via alias.
  const mods = new Set<Modifier>();
  let key = '';
  for (const part of parts) {
    const mod = MODIFIER_ALIASES[part.toLowerCase()];
    if (mod) mods.add(mod);
    else key = normalizeKeyToken(part);
  }
  if (!key) return '';
  const ordered = MODIFIER_ORDER.filter((m) => mods.has(m));
  return [...ordered, key].join('+');
}

/**
 * Build a canonical binding from a keydown event, for "press the new
 * shortcut" recording. Returns null while only modifiers are held.
 * (Escape is returned as 'Esc' — callers that treat Escape as "cancel
 * recording" should check `e.key` before calling.)
 */
export function bindingFromKeyboardEvent(e: KeyboardEvent): string | null {
  // `KeyboardEvent.key` is typed as always-present but is genuinely absent on
  // some events reaching a global listener (IME composition, and synthetic
  // events dispatched by extensions or tests). Without this guard
  // `normalizeKeyToken` did `undefined.trim()` and threw an uncaught
  // TypeError out of the document keydown handler on every such keystroke.
  if (typeof e.key !== 'string') return null;
  if (e.key === 'Control' || e.key === 'Shift' || e.key === 'Alt' || e.key === 'Meta') {
    return null;
  }
  const parts: string[] = [];
  if (e.ctrlKey) parts.push('Ctrl');
  if (e.altKey) parts.push('Alt');
  if (e.shiftKey) parts.push('Shift');
  if (e.metaKey) parts.push('Meta');
  const key = normalizeKeyToken(e.key);
  if (!key) return null;
  parts.push(key);
  return normalizeBinding(parts.join('+'));
}

/**
 * Human-readable form of a binding, e.g. 'Ctrl+Shift+D' → 'Ctrl + Shift + D'.
 * Deterministic (no platform sniffing) so it is safe to render during
 * prerender without hydration drift.
 */
export function formatBinding(binding: string): string {
  const canonical = normalizeBinding(binding);
  if (!canonical) return '';
  return canonical.split('+').join(' + ');
}

/* ── Persistence (localStorage overrides) ───────────────────────────────── */

// In-memory mirror of the stored overrides. localStorage is best-effort in
// some webview origins (writes can silently fail), so the live value is held
// here and storage only persists it when available — same pattern as
// lib/dictation/sttMode.ts.
let memOverrides: Record<string, string> | null = null;

function readStorage(): Record<string, string> {
  if (typeof window === 'undefined') return {};
  try {
    const raw = window.localStorage.getItem(HOTKEY_STORAGE_KEY);
    if (!raw) return {};
    const parsed: unknown = JSON.parse(raw);
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) return {};
    const out: Record<string, string> = {};
    for (const [id, value] of Object.entries(parsed as Record<string, unknown>)) {
      if (typeof value !== 'string') continue;
      const canonical = normalizeBinding(value);
      if (canonical && HOTKEYS.some((h) => h.id === id)) out[id] = canonical;
    }
    return out;
  } catch {
    return {};
  }
}

function overrides(): Record<string, string> {
  if (memOverrides === null) memOverrides = readStorage();
  return memOverrides;
}

function persist(next: Record<string, string>): void {
  memOverrides = next;
  if (typeof window === 'undefined') return;
  try {
    if (Object.keys(next).length === 0) {
      window.localStorage.removeItem(HOTKEY_STORAGE_KEY);
    } else {
      window.localStorage.setItem(HOTKEY_STORAGE_KEY, JSON.stringify(next));
    }
  } catch {
    /* storage unavailable — the in-memory value above still applies */
  }
  window.dispatchEvent(new CustomEvent(HOTKEYS_CHANGE_EVENT));
}

/** Effective bindings for every registered hotkey (defaults + overrides). */
export function getBindings(): Record<string, string> {
  const ov = overrides();
  const out: Record<string, string> = {};
  for (const def of HOTKEYS) {
    out[def.id] = ov[def.id] ?? normalizeBinding(def.defaultBinding);
  }
  return out;
}

/** Effective binding for one hotkey id ('' if the id is unknown). */
export function getBinding(id: string): string {
  const ov = overrides();
  if (ov[id]) return ov[id];
  const def = HOTKEYS.find((h) => h.id === id);
  return def ? normalizeBinding(def.defaultBinding) : '';
}

/**
 * Set a custom binding for a hotkey. Setting a binding equal to the default
 * removes the override instead, so "Reset" and re-typing the default agree.
 */
export function setBinding(id: string, binding: string): void {
  const def = HOTKEYS.find((h) => h.id === id);
  if (!def) return;
  const canonical = normalizeBinding(binding);
  const next = { ...overrides() };
  if (!canonical || canonical === normalizeBinding(def.defaultBinding)) {
    delete next[id];
  } else {
    next[id] = canonical;
  }
  persist(next);
}

/** Remove every custom binding and return to defaults. */
export function resetAll(): void {
  persist({});
}

export interface HotkeyConflict {
  binding: string;
  /** Ids of the (implemented) actions sharing this binding. */
  ids: string[];
}

/**
 * Find bindings assigned to two or more enabled (implemented) actions.
 * Pass a candidate id→binding map to check unsaved edits; defaults to the
 * currently effective bindings.
 */
export function findConflicts(bindings: Record<string, string> = getBindings()): HotkeyConflict[] {
  const byBinding = new Map<string, string[]>();
  for (const def of HOTKEYS) {
    if (!def.implemented) continue;
    const raw = bindings[def.id] ?? normalizeBinding(def.defaultBinding);
    const canonical = normalizeBinding(raw);
    if (!canonical) continue;
    const list = byBinding.get(canonical) ?? [];
    list.push(def.id);
    byBinding.set(canonical, list);
  }
  const out: HotkeyConflict[] = [];
  for (const [binding, ids] of byBinding) {
    if (ids.length > 1) out.push({ binding, ids });
  }
  return out;
}
