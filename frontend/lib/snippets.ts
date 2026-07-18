// F3 — per-user text snippets (autotext) with tab-through fill-in fields.
//
// A snippet is a short trigger + a body; the body may contain ${field} placeholders the radiologist
// tabs through after inserting it (e.g. "There is a ${size} nodule in the ${location}."). Snippets
// are DEVICE-LOCAL (localStorage), like hotkeys and STT prefs — never PHI, and matching the brief's
// "local per-user phrase memory". The field-parsing + selection math is pure and unit-tested; the
// storage layer mirrors lib/hotkeys.ts (in-memory mirror + best-effort persistence + change event).

export interface Snippet {
  id: string;
  /** Short abbreviation the radiologist types/says, e.g. "nlchest". */
  trigger: string;
  /** Expansion; may contain ${field} placeholders. */
  body: string;
}

export const SNIPPET_STORAGE_KEY = 'rp-snippets';
export const SNIPPETS_CHANGE_EVENT = 'rp-snippets-change';

export interface FieldRange {
  start: number;
  end: number;
  name: string;
}

/** Every ${field} placeholder range in `text`, in order. */
export function findFields(text: string): FieldRange[] {
  const out: FieldRange[] = [];
  const re = /\$\{[^}]*\}/g;
  let m: RegExpExecArray | null;
  while ((m = re.exec(text)) !== null) {
    out.push({ start: m.index, end: m.index + m[0].length, name: m[0].slice(2, -1) });
    if (m.index === re.lastIndex) re.lastIndex++; // guard against a zero-width match looping
  }
  return out;
}

export interface Expansion {
  value: string;
  selectionStart: number;
  selectionEnd: number;
}

/**
 * Insert `body` into `value`, replacing the range [selStart, selEnd). If the body has a ${field},
 * the first one is selected (ready to type over); otherwise the caret lands just after the insert.
 */
export function expandSnippet(value: string, selStart: number, selEnd: number, body: string): Expansion {
  const before = value.slice(0, selStart);
  const after = value.slice(selEnd);
  const nextValue = before + body + after;
  const fields = findFields(body);
  if (fields.length > 0) {
    const f = fields[0];
    return {
      value: nextValue,
      selectionStart: before.length + f.start,
      selectionEnd: before.length + f.end,
    };
  }
  const caret = before.length + body.length;
  return { value: nextValue, selectionStart: caret, selectionEnd: caret };
}

/**
 * The next ${field} placeholder at/after `fromCaret`, wrapping to the first when none remain ahead.
 * Returns null when there are no fields left (all filled). Drives Tab-to-next-field.
 */
export function nextFieldSelection(value: string, fromCaret: number): { start: number; end: number } | null {
  const fields = findFields(value);
  if (fields.length === 0) return null;
  const ahead = fields.find((f) => f.start >= fromCaret);
  const f = ahead ?? fields[0];
  return { start: f.start, end: f.end };
}

// ── Storage (device-local; mirrors lib/hotkeys.ts) ──────────────────────────

let mem: Snippet[] | null = null;

function read(): Snippet[] {
  if (typeof window === 'undefined') return [];
  try {
    const raw = window.localStorage.getItem(SNIPPET_STORAGE_KEY);
    if (!raw) return [];
    const parsed: unknown = JSON.parse(raw);
    if (!Array.isArray(parsed)) return [];
    return parsed.filter(
      (s): s is Snippet =>
        !!s &&
        typeof (s as Snippet).id === 'string' &&
        typeof (s as Snippet).trigger === 'string' &&
        typeof (s as Snippet).body === 'string',
    );
  } catch {
    return [];
  }
}

function persist(next: Snippet[]): void {
  mem = next;
  if (typeof window === 'undefined') return;
  try {
    window.localStorage.setItem(SNIPPET_STORAGE_KEY, JSON.stringify(next));
  } catch {
    /* storage unavailable — the in-memory value still applies this session */
  }
  window.dispatchEvent(new CustomEvent(SNIPPETS_CHANGE_EVENT));
}

export function getSnippets(): Snippet[] {
  if (mem === null) mem = read();
  return mem;
}

/** Upsert by id (or by trigger when adding). Trigger is trimmed; body kept verbatim. */
export function saveSnippet(input: { id?: string; trigger: string; body: string }): Snippet {
  const list = getSnippets().slice();
  const trigger = input.trigger.trim();
  const idx = input.id
    ? list.findIndex((s) => s.id === input.id)
    : list.findIndex((s) => s.trigger.toLowerCase() === trigger.toLowerCase());
  let saved: Snippet;
  if (idx >= 0) {
    saved = { ...list[idx], trigger, body: input.body };
    list[idx] = saved;
  } else {
    saved = { id: newId(), trigger, body: input.body };
    list.push(saved);
  }
  persist(list);
  return saved;
}

export function deleteSnippet(id: string): void {
  persist(getSnippets().filter((s) => s.id !== id));
}

/** Exact (case-insensitive) trigger lookup — used by editor auto-expansion. */
export function findSnippetByTrigger(trigger: string): Snippet | undefined {
  const t = trigger.trim().toLowerCase();
  if (!t) return undefined;
  return getSnippets().find((s) => s.trigger.trim().toLowerCase() === t);
}

/** Test-only reset of the in-memory mirror. */
export function _resetSnippets(): void {
  mem = null;
}

function newId(): string {
  try {
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') return crypto.randomUUID();
  } catch {
    /* fall through */
  }
  return `s_${Math.random().toString(36).slice(2)}`;
}
