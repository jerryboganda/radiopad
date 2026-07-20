// DESK-020 — foot-pedal bindings drive the dictation overlay through its
// public toggle event with true hold-to-talk semantics.
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import {
  attachFootPedal,
  getFootPedalBindings,
  resetFootPedalBindings,
  setFootPedalBinding,
} from '@/lib/dictation/footPedal';
import {
  registerSectionEditor,
  _resetSectionEditorRegistry,
} from '@/lib/editor/sectionEditorRegistry';

function pressKey(type: 'keydown' | 'keyup', code: string, repeat = false) {
  window.dispatchEvent(new KeyboardEvent(type, { code, repeat }));
}

function broadcastListening(listening: boolean) {
  window.dispatchEvent(new CustomEvent('radiopad:dictate-listening', { detail: { listening } }));
}

describe('foot pedal', () => {
  let toggles = 0;
  let detach: () => void;
  const onToggle = () => { toggles += 1; };

  beforeEach(() => {
    resetFootPedalBindings();
    toggles = 0;
    window.addEventListener('radiopad:dictate', onToggle);
    detach = attachFootPedal();
  });

  afterEach(() => {
    detach();
    window.removeEventListener('radiopad:dictate', onToggle);
    _resetSectionEditorRegistry();
    resetFootPedalBindings();
  });

  it('defaults to F13/F14/F15', () => {
    expect(getFootPedalBindings()).toEqual({
      holdToTalk: 'F13',
      toggleDictation: 'F14',
      nextField: 'F15',
    });
  });

  it('hold-to-talk: down starts, up (after the overlay reports listening) stops', () => {
    pressKey('keydown', 'F13');
    expect(toggles).toBe(1);
    broadcastListening(true);
    pressKey('keyup', 'F13');
    expect(toggles).toBe(2);
  });

  it('hold-to-talk ignores key repeats while held', () => {
    pressKey('keydown', 'F13');
    pressKey('keydown', 'F13', true);
    pressKey('keydown', 'F13', true);
    expect(toggles).toBe(1);
  });

  it('a quick tap (release before the overlay reports listening) leaves the mic on', () => {
    pressKey('keydown', 'F13');
    pressKey('keyup', 'F13'); // listening broadcast has not arrived yet
    expect(toggles).toBe(1);
  });

  it('toggle pedal toggles on every press', () => {
    pressKey('keydown', 'F14');
    broadcastListening(true);
    pressKey('keydown', 'F14');
    expect(toggles).toBe(2);
  });

  it('next-field pedal focuses the next section editor', () => {
    const focused: string[] = [];
    registerSectionEditor({
      sectionKey: 'findings',
      insertAtCursor: () => {},
      focus: () => focused.push('findings'),
    });
    pressKey('keydown', 'F15');
    expect(focused).toEqual(['findings']);
    expect(toggles).toBe(0);
  });

  it('rebinding moves the action to the new code and unbound codes do nothing', () => {
    setFootPedalBinding('toggleDictation', 'F20');
    pressKey('keydown', 'F14');
    expect(toggles).toBe(0);
    pressKey('keydown', 'F20');
    expect(toggles).toBe(1);

    setFootPedalBinding('holdToTalk', '');
    pressKey('keydown', 'F13');
    expect(toggles).toBe(1);
  });
});
