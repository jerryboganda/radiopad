// F1 (dictation brief) — "scratch that" undo. Must recognise the command as a whole utterance
// and must NOT fire on ordinary dictation that merely contains a keyword, which would silently
// destroy the radiologist's text.
import { describe, it, expect } from 'vitest';
import { parseVoiceEditCommand } from '@/lib/dictation/voiceEditCommands';

describe('parseVoiceEditCommand — recognised commands', () => {
  it.each([
    'scratch that',
    'Scratch that.',
    'scratch',
    'strike that',
    'undo',
    'undo that',
    'delete that',
    '  scratch that!  ',
  ])('treats "%s" as an undo command', (phrase) => {
    expect(parseVoiceEditCommand(phrase)).toEqual({ kind: 'undo' });
  });
});

describe('parseVoiceEditCommand — leaves dictation content alone', () => {
  it.each([
    '',
    '   ',
    'delete the prior comparison study',
    'there is no scratch on the cortical surface',
    'undo the effect of the contrast',
    'the lesion measures 3 cm',
    'that scratch is superficial',
  ])('does not treat "%s" as a command', (phrase) => {
    expect(parseVoiceEditCommand(phrase)).toBeNull();
  });
});
