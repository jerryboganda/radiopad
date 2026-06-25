import { describe, it, expect, vi } from 'vitest';
import { insertAtCursor } from '@/lib/dictation/insertText';

function textarea(value = '', selStart = value.length, selEnd = selStart): HTMLTextAreaElement {
  const el = document.createElement('textarea');
  el.value = value;
  document.body.appendChild(el);
  el.setSelectionRange(selStart, selEnd);
  return el;
}

describe('insertAtCursor', () => {
  it('inserts into an empty field and moves the caret to the end', () => {
    const el = textarea('');
    insertAtCursor(el, 'hello');
    expect(el.value).toBe('hello');
    expect(el.selectionStart).toBe(5);
    el.remove();
  });

  it('adds a separating space when appending to existing prose', () => {
    const el = textarea('abc');
    insertAtCursor(el, 'def');
    expect(el.value).toBe('abc def');
    el.remove();
  });

  it('does not double the space when prose already ends with whitespace', () => {
    const el = textarea('abc ');
    insertAtCursor(el, 'def');
    expect(el.value).toBe('abc def');
    el.remove();
  });

  it('does not add a space before punctuation', () => {
    const el = textarea('abc');
    insertAtCursor(el, '.');
    expect(el.value).toBe('abc.');
    el.remove();
  });

  it('replaces the current selection', () => {
    const el = textarea('abcxyz', 0, 6);
    insertAtCursor(el, 'new');
    expect(el.value).toBe('new');
    el.remove();
  });

  it('dispatches a bubbling input event so frameworks observe the change', () => {
    const el = textarea('');
    const onInput = vi.fn();
    el.addEventListener('input', onInput);
    insertAtCursor(el, 'x');
    expect(onInput).toHaveBeenCalledTimes(1);
    el.remove();
  });

  it('inserts in the middle at the caret', () => {
    const el = textarea('ad', 1, 1);
    insertAtCursor(el, 'bc', { smartSpacing: false });
    expect(el.value).toBe('abcd');
    expect(el.selectionStart).toBe(3);
    el.remove();
  });
});
