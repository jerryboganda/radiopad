import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import {
  isDictationTarget,
  startFocusTracking,
  getLastFocusedEditable,
  _resetFocusTracker,
} from '@/lib/dictation/focusTracker';

function focusin(el: HTMLElement): void {
  el.dispatchEvent(new FocusEvent('focusin', { bubbles: true }));
}

describe('isDictationTarget', () => {
  it('accepts textareas and text-like inputs, rejects others', () => {
    const ta = document.createElement('textarea');
    const text = document.createElement('input');
    text.type = 'text';
    const checkbox = document.createElement('input');
    checkbox.type = 'checkbox';
    const button = document.createElement('button');

    expect(isDictationTarget(ta)).toBe(true);
    expect(isDictationTarget(text)).toBe(true);
    expect(isDictationTarget(checkbox)).toBe(false);
    expect(isDictationTarget(button)).toBe(false);
    expect(isDictationTarget(null)).toBe(false);
  });
});

describe('focus tracking', () => {
  let stop: () => void;

  beforeEach(() => {
    _resetFocusTracker();
    stop = startFocusTracking();
  });
  afterEach(() => {
    stop();
    document.body.innerHTML = '';
  });

  it('remembers the last focused editable element', () => {
    const ta = document.createElement('textarea');
    document.body.appendChild(ta);
    focusin(ta);
    expect(getLastFocusedEditable()).toBe(ta);
  });

  it('ignores focus on non-editable elements (e.g. the FAB button)', () => {
    const ta = document.createElement('textarea');
    const btn = document.createElement('button');
    document.body.append(ta, btn);
    focusin(ta);
    focusin(btn);
    expect(getLastFocusedEditable()).toBe(ta);
  });

  it('returns null once the tracked element leaves the DOM', () => {
    const ta = document.createElement('textarea');
    document.body.appendChild(ta);
    focusin(ta);
    ta.remove();
    expect(getLastFocusedEditable()).toBeNull();
  });
});
