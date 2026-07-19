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

  it('preserves the line structure of a MULTI-LINE snippet body', async () => {
    // The whole point of a canned block is that it is multi-line, and the original test only ever
    // used a single-line body — so nothing pinned this.
    //
    // Worth knowing: an audit flagged the previous raw-string insertion as flattening newlines
    // (plainText.ts documents that as Tiptap's default). Empirically it did NOT — this test passes
    // against both implementations. The insertion was still made explicit via stringToDoc so the
    // behaviour no longer depends on Tiptap's incidental string parsing, and this test is what
    // guarantees it stays correct through a version bump either way.
    const body = 'Lungs: Clear.\nHeart: Normal size.\nBones: No acute abnormality.';
    saveSnippet({ trigger: 'nlchest', body });
    const onChange = vi.fn();
    render(<SectionEditor sectionKey="findings" value="" onChange={onChange} />);
    await screen.findByRole('textbox');

    act(() => {
      noteSectionEditorFocus('findings');
      getLastFocusedSectionEditor()?.insertAtCursor('nlchest');
    });

    fireEvent.keyDown(screen.getByRole('textbox'), { key: 'Tab' });

    await waitFor(() => {
      const last = onChange.mock.calls.at(-1)?.[0] as string | undefined;
      expect(last ?? '').toContain('Bones: No acute abnormality.');
    });

    const final = onChange.mock.calls.at(-1)?.[0] as string;
    // Each authored line must remain its own line, not be joined by spaces.
    expect(final).toContain('Lungs: Clear.\nHeart: Normal size.');
    expect(final).not.toContain('Clear. Heart:');
  });
});
