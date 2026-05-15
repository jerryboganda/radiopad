/**
 * Tests for the textDiff utility and Prompt Studio page (PRD §16.4).
 */
import { describe, it, expect } from 'vitest';
import { computeDiff } from '@/lib/textDiff';

describe('computeDiff', () => {
  it('returns same for identical strings', () => {
    const result = computeDiff('hello\nworld', 'hello\nworld');
    expect(result).toEqual([
      { type: 'same', text: 'hello' },
      { type: 'same', text: 'world' },
    ]);
  });

  it('detects added lines', () => {
    const result = computeDiff('hello', 'hello\nworld');
    expect(result).toEqual([
      { type: 'same', text: 'hello' },
      { type: 'added', text: 'world' },
    ]);
  });

  it('detects removed lines', () => {
    const result = computeDiff('hello\nworld', 'hello');
    expect(result).toEqual([
      { type: 'same', text: 'hello' },
      { type: 'removed', text: 'world' },
    ]);
  });

  it('detects mixed changes', () => {
    const result = computeDiff('a\nb\nc', 'a\nx\nc');
    expect(result).toHaveLength(4); // same, removed, added, same
    expect(result[0]).toEqual({ type: 'same', text: 'a' });
    expect(result[result.length - 1]).toEqual({ type: 'same', text: 'c' });
    const types = result.map((r) => r.type);
    expect(types).toContain('removed');
    expect(types).toContain('added');
  });

  it('handles empty strings', () => {
    const result = computeDiff('', '');
    expect(result).toEqual([{ type: 'same', text: '' }]);
  });

  it('handles one side empty', () => {
    const result = computeDiff('', 'hello');
    // '' splits to [''], so we get a removed empty line + an added 'hello'
    const added = result.filter((r) => r.type === 'added');
    expect(added).toEqual([{ type: 'added', text: 'hello' }]);
  });

  it('handles entirely different content', () => {
    const result = computeDiff('aaa\nbbb', 'xxx\nyyy');
    const added = result.filter((r) => r.type === 'added');
    const removed = result.filter((r) => r.type === 'removed');
    expect(added.length).toBe(2);
    expect(removed.length).toBe(2);
  });
});
