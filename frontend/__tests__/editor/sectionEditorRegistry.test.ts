import { describe, it, expect, beforeEach, vi } from 'vitest';
import {
  registerSectionEditor,
  unregisterSectionEditor,
  noteSectionEditorFocus,
  getLastFocusedSectionEditor,
  getSectionEditor,
  _resetSectionEditorRegistry,
  type SectionEditorHandle,
} from '@/lib/editor/sectionEditorRegistry';

function handle(sectionKey: string): SectionEditorHandle {
  return { sectionKey, insertAtCursor: vi.fn(), focus: vi.fn() };
}

beforeEach(() => _resetSectionEditorRegistry());

describe('sectionEditorRegistry', () => {
  it('starts with no focused editor', () => {
    expect(getLastFocusedSectionEditor()).toBeNull();
  });

  it('tracks the most recently focused editor', () => {
    const a = handle('findings');
    const b = handle('impression');
    registerSectionEditor(a);
    registerSectionEditor(b);
    noteSectionEditorFocus('findings');
    expect(getLastFocusedSectionEditor()).toBe(a);
    noteSectionEditorFocus('impression');
    expect(getLastFocusedSectionEditor()).toBe(b);
  });

  it('ignores focus for an unregistered key', () => {
    noteSectionEditorFocus('ghost');
    expect(getLastFocusedSectionEditor()).toBeNull();
  });

  it('clears the last focused editor when it unregisters', () => {
    const a = handle('findings');
    registerSectionEditor(a);
    noteSectionEditorFocus('findings');
    unregisterSectionEditor('findings');
    expect(getLastFocusedSectionEditor()).toBeNull();
    expect(getSectionEditor('findings')).toBeUndefined();
  });
});
