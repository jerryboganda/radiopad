// P0.3 — push-to-talk tap/hold discrimination. A quick tap must toggle; a hold must dictate only
// while held; keyboard clicks (no pointer events) must still toggle.
import { describe, it, expect, vi } from 'vitest';
import { createPushToTalk } from '@/lib/dictation/pushToTalk';

function harness(initialActive = false) {
  let active = initialActive;
  let t = 0;
  const start = vi.fn(() => {
    active = true;
  });
  const stop = vi.fn(() => {
    active = false;
  });
  const ptt = createPushToTalk({
    isActive: () => active,
    start,
    stop,
    holdMs: 300,
    now: () => t,
  });
  return {
    ptt,
    start,
    stop,
    advance: (ms: number) => {
      t += ms;
    },
    isActive: () => active,
  };
}

describe('createPushToTalk', () => {
  it('quick tap on an idle mic toggles it ON (and leaves it running)', () => {
    const h = harness(false);
    h.ptt.pointerDown();
    h.advance(80); // released quickly
    h.ptt.pointerUp();
    expect(h.start).toHaveBeenCalledTimes(1);
    expect(h.stop).not.toHaveBeenCalled();
    expect(h.isActive()).toBe(true);
  });

  it('quick tap on an active mic toggles it OFF', () => {
    const h = harness(true);
    h.ptt.pointerDown();
    h.advance(80);
    h.ptt.pointerUp();
    expect(h.start).not.toHaveBeenCalled();
    expect(h.stop).toHaveBeenCalledTimes(1);
    expect(h.isActive()).toBe(false);
  });

  it('press-and-hold dictates only while held (starts on down, stops on release)', () => {
    const h = harness(false);
    h.ptt.pointerDown();
    expect(h.start).toHaveBeenCalledTimes(1);
    expect(h.isActive()).toBe(true);
    h.advance(500); // held past the threshold
    h.ptt.pointerUp();
    expect(h.stop).toHaveBeenCalledTimes(1);
    expect(h.isActive()).toBe(false);
  });

  it('holding while already active ends on release', () => {
    const h = harness(true);
    h.ptt.pointerDown();
    expect(h.start).not.toHaveBeenCalled();
    h.advance(500);
    h.ptt.pointerUp();
    expect(h.stop).toHaveBeenCalledTimes(1);
  });

  it('a stray pointerUp with no preceding down is a no-op', () => {
    const h = harness(false);
    h.ptt.pointerUp();
    expect(h.start).not.toHaveBeenCalled();
    expect(h.stop).not.toHaveBeenCalled();
  });

  it('claimClick consumes the pointer-driven click once, then releases it', () => {
    const h = harness(false);
    h.ptt.pointerDown();
    h.ptt.pointerUp();
    expect(h.ptt.claimClick()).toBe(true); // the click that follows a pointer sequence
    expect(h.ptt.claimClick()).toBe(false); // a later, keyboard-driven click
  });

  it('claimClick returns false for a keyboard click with no pointer sequence', () => {
    const h = harness(false);
    expect(h.ptt.claimClick()).toBe(false);
  });
});
