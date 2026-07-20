// F3 — insert a snippet into a plain <textarea>/<input> and tab through its ${field} placeholders.
// React-safe (uses the native value setter + a bubbling input event, like lib/dictation/insertText),
// so it works with controlled inputs. The rich (ProseMirror) editors get the same behaviour from
// lib/editor/snippetExpansion; the two share the trigger-recognition logic and must stay in step.
//
// `snippetKeyDown` is the entry point a textarea binds to Tab. It is wired into the report editor's
// plain-section fallback (ReportClient), which is what a radiologist sees with the rich editor
// switched off. Until then this module claimed to cover "every plain-textarea surface" while having
// no callers at all, so that surface simply had no snippets.

import { expandSnippet, nextFieldSelection } from './snippets';
import { computeTriggerExpansion } from './editor/snippetExpansion';

type EditableEl = HTMLTextAreaElement | HTMLInputElement;

function setNativeValue(el: EditableEl, value: string): void {
  const proto = el instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
  const desc = Object.getOwnPropertyDescriptor(proto, 'value');
  if (desc?.set) desc.set.call(el, value);
  else el.value = value;
}

/** Replace [from, to) with `body`, selecting its first ${field}. */
function replaceRangeWithSnippet(el: EditableEl, from: number, to: number, body: string): void {
  const r = expandSnippet(el.value, from, to, body);
  setNativeValue(el, r.value);
  try {
    el.setSelectionRange(r.selectionStart, r.selectionEnd);
  } catch {
    /* setSelectionRange throws for some input types — best-effort caret */
  }
  el.dispatchEvent(new Event('input', { bubbles: true }));
}

/** Insert `body` at the element's caret (replacing any selection), selecting the first ${field}. */
export function insertSnippet(el: EditableEl, body: string): void {
  const start = el.selectionStart ?? el.value.length;
  const end = el.selectionEnd ?? start;
  replaceRangeWithSnippet(el, start, end, body);
}

/** Select the next ${field} ahead of the caret. Returns false when none remain (Tab falls through). */
export function snippetTab(el: EditableEl): boolean {
  const from = el.selectionEnd ?? 0;
  const sel = nextFieldSelection(el.value, from);
  if (!sel) return false;
  try {
    el.setSelectionRange(sel.start, sel.end);
  } catch {
    /* best-effort */
  }
  return true;
}

/**
 * Handle Tab in a plain textarea/input: expand the trigger word before the caret, else advance to
 * the next ${field}. Returns true when it acted (the caller must preventDefault), false to let Tab
 * do its normal thing — including moving focus out, which must always stay possible.
 *
 * This is the textarea twin of the `SnippetExpansion` Tiptap extension. The report editor renders
 * plain textareas whenever the radiologist turns the rich editor off, and that surface had no
 * snippet support at all: this module's own header claimed to cover "every plain-textarea surface"
 * while nothing in the product ever called it, so switching editors silently removed the feature.
 */
export function snippetKeyDown(el: EditableEl): boolean {
  const start = el.selectionStart ?? el.value.length;
  const end = el.selectionEnd ?? start;

  if (start === end) {
    const match = computeTriggerExpansion(el.value.slice(0, start));
    if (match) {
      replaceRangeWithSnippet(el, start - match.word.length, start, match.body);
      return true;
    }
  }
  return snippetTab(el);
}
