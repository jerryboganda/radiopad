// Tracks the mounted rich section editors and the last one the radiologist
// focused. The dictation overlay uses this the same way it uses focusTracker
// for plain textareas: at insertion time `document.activeElement` is the mic
// button, so we remember the most recently focused editor instead.
//
// Rich editors render a `contenteditable` div, not a <textarea>, so the plain
// `focusTracker` (which only matches textarea/input) never sees them — the two
// trackers are deliberately independent and the overlay prefers this one.

export interface SectionEditorHandle {
  /** Report section key this editor edits (findings, impression, …). */
  sectionKey: string;
  /** Insert dictated text at the caret (with smart spacing). */
  insertAtCursor: (text: string) => void;
  /** Move focus into the editor. */
  focus: () => void;
  /** Show/replace the live (interim) dictation preview at the caret. Optional —
   *  editors that don't render a real-time preview simply omit it. */
  setInterim?: (text: string) => void;
  /** Clear the interim preview (called on a final result / when dictation stops). */
  clearInterim?: () => void;
  /** Start a new line/paragraph at the caret (companion "New line" remote). */
  newLine?: () => void;
  /** Undo the last change in this editor (companion "Undo" remote). */
  undo?: () => void;
}

const registry = new Map<string, SectionEditorHandle>();
let lastFocused: SectionEditorHandle | null = null;

export function registerSectionEditor(handle: SectionEditorHandle): void {
  registry.set(handle.sectionKey, handle);
}

export function unregisterSectionEditor(sectionKey: string): void {
  registry.delete(sectionKey);
  if (lastFocused?.sectionKey === sectionKey) lastFocused = null;
}

export function noteSectionEditorFocus(sectionKey: string): void {
  const handle = registry.get(sectionKey);
  if (handle) lastFocused = handle;
}

/** The last focused section editor, or null if none is mounted/focused. */
export function getLastFocusedSectionEditor(): SectionEditorHandle | null {
  return lastFocused;
}

export function getSectionEditor(sectionKey: string): SectionEditorHandle | undefined {
  return registry.get(sectionKey);
}

/** Mounted section editors in registration order (used by companion remote
 *  next/prev-section navigation). */
export function getSectionEditorsInOrder(): SectionEditorHandle[] {
  return [...registry.values()];
}

/**
 * Move focus to the next/previous mounted section editor relative to the
 * last-focused one (wraps around; starts at the first when none is focused).
 * Shared by the companion remote and the voice navigation commands.
 * Returns the focused section key, or null when no editors are mounted.
 */
export function focusAdjacentSection(direction: 1 | -1): string | null {
  const editors = getSectionEditorsInOrder();
  if (editors.length === 0) return null;
  const current = getLastFocusedSectionEditor();
  const idx = current ? editors.findIndex((e) => e.sectionKey === current.sectionKey) : -1;
  const nextIdx = idx < 0 ? 0 : (idx + direction + editors.length) % editors.length;
  const target = editors[nextIdx];
  target.focus();
  return target.sectionKey;
}

/** Test-only reset of module state. */
export function _resetSectionEditorRegistry(): void {
  registry.clear();
  lastFocused = null;
}
