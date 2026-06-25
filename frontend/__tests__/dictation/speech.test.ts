import { describe, it, expect, afterEach } from 'vitest';
import { parseSpeechResults, getSpeechRecognitionCtor } from '@/lib/dictation/speech';

// Build a Web-Speech-style result event from [transcript, isFinal] tuples.
function speechEvent(items: Array<[string, boolean]>, resultIndex = 0) {
  return {
    resultIndex,
    results: items.map(([transcript, isFinal]) =>
      Object.assign([{ transcript }], { isFinal }),
    ),
  };
}

describe('parseSpeechResults', () => {
  it('separates final and interim transcripts', () => {
    const event = speechEvent([
      ['lungs are clear', true],
      ['there is', false],
    ]);
    expect(parseSpeechResults(event)).toEqual({
      finalText: 'lungs are clear',
      interimText: 'there is',
    });
  });

  it('honours resultIndex so already-consumed results are skipped', () => {
    const event = speechEvent(
      [
        ['old final', true],
        ['new final', true],
      ],
      1,
    );
    expect(parseSpeechResults(event)).toEqual({ finalText: 'new final', interimText: '' });
  });

  it('returns empty strings for an empty result set', () => {
    expect(parseSpeechResults(speechEvent([]))).toEqual({ finalText: '', interimText: '' });
  });
});

describe('getSpeechRecognitionCtor', () => {
  const w = window as unknown as Record<string, unknown>;
  afterEach(() => {
    delete w.SpeechRecognition;
    delete w.webkitSpeechRecognition;
  });

  it('returns null when no Web Speech API is present', () => {
    expect(getSpeechRecognitionCtor()).toBeNull();
  });

  it('prefers the standard constructor, then the webkit-prefixed one', () => {
    const standard = function () {} as unknown;
    w.SpeechRecognition = standard;
    expect(getSpeechRecognitionCtor()).toBe(standard);
    delete w.SpeechRecognition;
    const webkit = function () {} as unknown;
    w.webkitSpeechRecognition = webkit;
    expect(getSpeechRecognitionCtor()).toBe(webkit);
  });
});
