// P0.3 (dictation brief) — push-to-talk for the dictation mic. Adds hold-to-talk WITHOUT removing
// tap-to-toggle: a quick tap toggles hands-free listening; pressing and holding dictates only while
// held and stops on release. Framework-agnostic and deterministic (injectable clock) so the
// tap/hold discrimination is unit-tested independently of React.

export interface PushToTalkOptions {
  /** Whether dictation is currently active (read at event time — pass a ref getter). */
  isActive: () => boolean;
  /** Begin dictation (tap-on or hold-start). */
  start: () => void;
  /** End dictation (tap-off or hold-release). */
  stop: () => void;
  /** Press duration (ms) at/after which a press counts as a hold, not a tap. Default 300. */
  holdMs?: number;
  /** Clock; injectable for tests. Default Date.now. */
  now?: () => number;
}

export interface PushToTalkHandlers {
  pointerDown: () => void;
  pointerUp: () => void;
  /**
   * Call from the element's onClick. Returns true when a preceding pointer sequence already
   * handled the interaction (so the caller should NOT also toggle). Returns false for a
   * keyboard-driven click (Enter/Space), which arrives with no pointer events — letting the
   * caller toggle so the control stays keyboard-accessible.
   */
  claimClick: () => boolean;
}

export function createPushToTalk(opts: PushToTalkOptions): PushToTalkHandlers {
  const holdMs = opts.holdMs ?? 300;
  const now = opts.now ?? (() => Date.now());

  let downAt: number | null = null;
  let startedByThisPress = false;
  let pointerHandled = false;

  return {
    pointerDown() {
      pointerHandled = false;
      downAt = now();
      startedByThisPress = false;
      if (!opts.isActive()) {
        opts.start();
        startedByThisPress = true;
      }
    },
    pointerUp() {
      if (downAt === null) return; // stray up (e.g. pointer released off-element without a down)
      const held = now() - downAt;
      downAt = null;
      pointerHandled = true;
      if (startedByThisPress) {
        // Capture began on this press: a quick tap = toggle-on (leave it running); a hold =
        // push-to-talk, so release stops it.
        if (held >= holdMs) opts.stop();
      } else {
        // Already active when pressed → this press ends it (tap-off or hold-end).
        opts.stop();
      }
    },
    claimClick() {
      if (pointerHandled) {
        pointerHandled = false;
        return true;
      }
      return false;
    },
  };
}
