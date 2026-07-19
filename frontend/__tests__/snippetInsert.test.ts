// F3 — insertion + tab-through against a real textarea element: the body lands at the caret, the
// first field is selected, and Tab advances through the remaining fields.
import { describe, it, expect, beforeEach } from 'vitest';
import { insertSnippet, snippetTab, snippetKeyDown } from '@/lib/snippetInsert';
import { saveSnippet, _resetSnippets, SNIPPET_STORAGE_KEY } from '@/lib/snippets';

function textarea(value = '', caret = value.length): HTMLTextAreaElement {
  const el = document.createElement('textarea');
  el.value = value;
  el.setSelectionRange(caret, caret);
  return el;
}

describe('insertSnippet', () => {
  it('inserts the body at the caret and selects the first field', () => {
    const el = textarea('Findings: ', 10);
    insertSnippet(el, 'a ${size} nodule in the ${lobe}.');
    expect(el.value).toBe('Findings: a ${size} nodule in the ${lobe}.');
    expect(el.value.slice(el.selectionStart, el.selectionEnd)).toBe('${size}');
  });

  it('fires an input event so React controlled state updates', () => {
    const el = textarea('', 0);
    let fired = 0;
    el.addEventListener('input', () => { fired++; });
    insertSnippet(el, 'No acute abnormality.');
    expect(fired).toBe(1);
    expect(el.value).toBe('No acute abnormality.');
    // No fields → caret at end.
    expect(el.selectionStart).toBe(el.value.length);
  });
});

describe('snippetTab', () => {
  it('advances through fields and reports when done', () => {
    const el = textarea('', 0);
    insertSnippet(el, 'a ${one} b ${two}');
    // First field already selected by insert.
    expect(el.value.slice(el.selectionStart, el.selectionEnd)).toBe('${one}');
    // Tab → second field.
    expect(snippetTab(el)).toBe(true);
    expect(el.value.slice(el.selectionStart, el.selectionEnd)).toBe('${two}');
    // Fill both fields, then Tab reports nothing left.
    el.value = 'a X b Y';
    el.setSelectionRange(7, 7);
    expect(snippetTab(el)).toBe(false);
  });
});

// The textarea twin of the Tiptap extension — the surface a radiologist gets when the rich editor
// is switched off. It had no snippet support at all: this module claimed to cover "every
// plain-textarea surface" and nothing in the product called it.
describe('snippetKeyDown', () => {
  beforeEach(() => {
    window.localStorage.removeItem(SNIPPET_STORAGE_KEY);
    _resetSnippets();
  });

  it('expands the trigger word before the caret', () => {
    saveSnippet({ trigger: 'nlchest', body: 'The ${vessel} is patent.' });
    const el = textarea('Findings: nlchest');

    expect(snippetKeyDown(el)).toBe(true);
    expect(el.value).toBe('Findings: The ${vessel} is patent.');
    // The first blank is selected, ready to type over.
    expect(el.value.slice(el.selectionStart, el.selectionEnd)).toBe('${vessel}');
  });

  it('advances to the next field, then releases Tab so focus can leave', () => {
    const el = textarea('a ${one} b ${two}', 0);

    expect(snippetKeyDown(el)).toBe(true); // → ${one}
    expect(snippetKeyDown(el)).toBe(true); // → ${two}
    // Nothing ahead — Tab belongs to the browser, or the radiologist is trapped in the section.
    expect(snippetKeyDown(el)).toBe(false);
  });

  it('leaves Tab alone when there is no trigger and no field', () => {
    saveSnippet({ trigger: 'nlchest', body: 'x' });
    const el = textarea('No acute abnormality.');
    expect(snippetKeyDown(el)).toBe(false);
  });
});
