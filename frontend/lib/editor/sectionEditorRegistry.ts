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

/** Test-only reset of module state. */
export function _resetSectionEditorRegistry(): void {
  registry.clear();
  lastFocused = null;
}
