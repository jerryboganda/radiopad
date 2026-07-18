// F3 — in-editor snippet auto-expansion. The recognition logic must identify the trigger word
// before the caret and resolve it against the store; the Tiptap extension must expand it on Tab.
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { render, screen, fireEvent, act, waitFor } from '@testing-library/react';
import SectionEditor from '@/components/editor/SectionEditor';
import {
  triggerWordBefore,
  computeTriggerExpansion,
} from '@/lib/editor/snippetExpansion';
import { saveSnippet, _resetSnippets, SNIPPET_STORAGE_KEY } from '@/lib/snippets';
import {
  getLastFocusedSectionEditor,
  noteSectionEditorFocus,
  _resetSectionEditorRegistry,
} from '@/lib/editor/sectionEditorRegistry';

beforeEach(() => {
  window.localStorage.removeItem(SNIPPET_STORAGE_KEY);
  _resetSnippets();
  _resetSectionEditorRegistry();
});

describe('triggerWordBefore', () => {
  it('returns the last word before the caret', () => {
    expect(triggerWordBefore('Findings: nlchest')).toBe('nlchest');
    expect(triggerWordBefore('one two three')).toBe('three');
  });
  it('is empty at a whitespace boundary or empty string', () => {
    expect(triggerWordBefore('word ')).toBe('');
    expect(triggerWordBefore('')).toBe('');
  });
});

describe('computeTriggerExpansion', () => {
  it('matches a stored trigger (case-insensitive) as the last word', () => {
    saveSnippet({ trigger: 'nlchest', body: 'The lungs are clear. ${finding}' });
    const r = computeTriggerExpansion('Impression: NLCHEST');
    expect(r?.word).toBe('NLCHEST');
    expect(r?.snippet.body).toContain('lungs are clear');
  });
  it('returns null when the last word is not a trigger', () => {
    saveSnippet({ trigger: 'nlchest', body: 'x' });
    expect(computeTriggerExpansion('the liver')).toBeNull();
  });
  it('returns null when a space already follows the trigger', () => {
    saveSnippet({ trigger: 'nlchest', body: 'x' });
    expect(computeTriggerExpansion('nlchest ')).toBeNull();
  });
});

describe('SectionEditor — Tab expands a snippet trigger', () => {
  it('replaces the trigger word with the snippet body on Tab', async () => {
    saveSnippet({ trigger: 'nlchest', body: 'The lungs are clear.' });
    const onChange = vi.fn();
    render(<SectionEditor sectionKey="findings" value="" onChange={onChange} />);
    await screen.findByRole('textbox');

    // Type the trigger via the imperative handle (leaves the caret right after it).
    act(() => {
      noteSectionEditorFocus('findings');
      getLastFocusedSectionEditor()?.insertAtCursor('nlchest');
    });

    fireEvent.keyDown(screen.getByRole('textbox'), { key: 'Tab' });

    await waitFor(() => {
      const last = onChange.mock.calls.at(-1)?.[0] as string | undefined;
      expect(last ?? '').toContain('The lungs are clear.');
    });
    // The raw trigger token is gone (expanded, not left behind).
    const final = onChange.mock.calls.at(-1)?.[0] as string;
    expect(final).not.toContain('nlchest');
  });
});
