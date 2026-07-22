import { describe, it, expect } from 'vitest';
import { canAutoApplyAiResult } from '@/lib/jobs';

// The clinical-safety gate behind the report-page apply flow (Phase 6.1): a
// finished AI result may only overwrite the editor field WITHOUT a manual
// confirm when it cannot clobber the radiologist's work. `before` is the field
// captured at submit; it is `undefined` on the ?aiJob= deep-link (opened fresh
// from the widget after finishing elsewhere).
describe('canAutoApplyAiResult — live-completion path (submit snapshot known)', () => {
  it('auto-applies when the field is byte-for-byte unchanged since submit', () => {
    expect(canAutoApplyAiResult('same text', 'same text')).toBe(true);
    expect(canAutoApplyAiResult('', '')).toBe(true);
  });

  it('routes to the preview when the field was edited under the running job', () => {
    expect(canAutoApplyAiResult('edited', 'original')).toBe(false);
    // even a whitespace-only change counts — never clobber a keystroke
    expect(canAutoApplyAiResult('original ', 'original')).toBe(false);
  });
});

describe('canAutoApplyAiResult — deep-link path (no submit snapshot)', () => {
  it('auto-applies only into a still-empty field', () => {
    expect(canAutoApplyAiResult('', undefined)).toBe(true);
    expect(canAutoApplyAiResult('   \n  ', undefined)).toBe(true);
  });

  it('routes a populated field to the preview instead of overwriting it', () => {
    expect(canAutoApplyAiResult('existing impression', undefined)).toBe(false);
  });
});
