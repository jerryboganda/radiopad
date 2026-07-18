// F3 — insert a snippet into a plain <textarea>/<input> and tab through its ${field} placeholders.
// React-safe (uses the native value setter + a bubbling input event, like lib/dictation/insertText),
// so it works with controlled inputs. The report editor is ProseMirror (rich); true trigger-on-type
// auto-expansion + tab-through THERE needs a dedicated editor plugin — this primitive covers every
// plain-textarea surface and proves the selection math against a real DOM element.

import { expandSnippet, nextFieldSelection } from './snippets';

type EditableEl = HTMLTextAreaElement | HTMLInputElement;

function setNativeValue(el: EditableEl, value: string): void {
  const proto = el instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
  const desc = Object.getOwnPropertyDescriptor(proto, 'value');
  if (desc?.set) desc.set.call(el, value);
  else el.value = value;
}

/** Insert `body` at the element's caret (replacing any selection), selecting the first ${field}. */
export function insertSnippet(el: EditableEl, body: string): void {
  const start = el.selectionStart ?? el.value.length;
  const end = el.selectionEnd ?? start;
  const r = expandSnippet(el.value, start, end, body);
  setNativeValue(el, r.value);
  try {
    el.setSelectionRange(r.selectionStart, r.selectionEnd);
  } catch {
    /* setSelectionRange throws for some input types — best-effort caret */
  }
  el.dispatchEvent(new Event('input', { bubbles: true }));
}

/** Select the next ${field} after the caret (wrapping). Returns false when all fields are filled. */
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
