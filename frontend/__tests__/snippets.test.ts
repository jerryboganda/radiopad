// F3 — snippet field parsing, expansion, and tab-through selection. The cursor-jump math is the
// novel part of the feature and must be exact; the store must round-trip and upsert by trigger.
import { describe, it, expect, beforeEach } from 'vitest';
import {
  findFields,
  expandSnippet,
  nextFieldSelection,
  getSnippets,
  saveSnippet,
  deleteSnippet,
  findSnippetByTrigger,
  _resetSnippets,
  SNIPPET_STORAGE_KEY,
} from '@/lib/snippets';

describe('findFields', () => {
  it('finds ${field} placeholders in order with names', () => {
    const fields = findFields('a ${size} b ${location} c');
    expect(fields.map((f) => f.name)).toEqual(['size', 'location']);
    expect('a ${size}'.slice(fields[0].start, fields[0].end)).toBe('${size}');
  });

  it('returns [] when there are no fields', () => {
    expect(findFields('no acute abnormality')).toEqual([]);
  });
});

describe('expandSnippet', () => {
  it('inserts the body and selects the first field', () => {
    const r = expandSnippet('Findings: ', 10, 10, 'a ${size} nodule');
    expect(r.value).toBe('Findings: a ${size} nodule');
    // The first field is selected, ready to type over.
    expect(r.value.slice(r.selectionStart, r.selectionEnd)).toBe('${size}');
  });

  it('places the caret after the body when there are no fields', () => {
    const r = expandSnippet('', 0, 0, 'No acute abnormality.');
    expect(r.value).toBe('No acute abnormality.');
    expect(r.selectionStart).toBe(r.value.length);
    expect(r.selectionEnd).toBe(r.value.length);
  });

  it('replaces the current selection', () => {
    const r = expandSnippet('replace me please', 8, 10, 'X'); // replace "me"
    expect(r.value).toBe('replace X please');
  });
});

describe('nextFieldSelection', () => {
  it('finds the next field at/after the caret', () => {
    const value = 'a ${one} b ${two}';
    const sel = nextFieldSelection(value, 3); // caret inside/after the first field start
    expect(value.slice(sel!.start, sel!.end)).toBe('${two}');
  });

  it('wraps to the first field when none remain ahead', () => {
    const value = 'a ${one} b ${two}';
    const sel = nextFieldSelection(value, value.length);
    expect(value.slice(sel!.start, sel!.end)).toBe('${one}');
  });

  it('returns null when all fields are filled', () => {
    expect(nextFieldSelection('all filled in', 0)).toBeNull();
  });
});

describe('snippet store', () => {
  beforeEach(() => {
    window.localStorage.removeItem(SNIPPET_STORAGE_KEY);
    _resetSnippets();
  });

  it('adds and lists snippets', () => {
    saveSnippet({ trigger: 'nl', body: 'No acute abnormality.' });
    expect(getSnippets().map((s) => s.trigger)).toEqual(['nl']);
  });

  it('upserts by trigger (case-insensitive) when adding a duplicate', () => {
    const first = saveSnippet({ trigger: 'nl', body: 'v1' });
    const second = saveSnippet({ trigger: 'NL', body: 'v2' });
    expect(second.id).toBe(first.id);
    expect(getSnippets()).toHaveLength(1);
    expect(getSnippets()[0].body).toBe('v2');
  });

  it('finds by trigger and deletes', () => {
    const s = saveSnippet({ trigger: 'chestnl', body: 'Lungs clear.' });
    expect(findSnippetByTrigger('CHESTNL')?.id).toBe(s.id);
    deleteSnippet(s.id);
    expect(getSnippets()).toEqual([]);
  });
});
