// F3 — in-editor snippet auto-expansion. The recognition logic must identify the trigger word
// before the caret and resolve it against the store; the Tiptap extension must expand it on Tab.
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { render, screen, fireEvent, act, waitFor } from '@testing-library/react';
import SectionEditor from '@/components/editor/SectionEditor';
import RichTextEditor from '@/components/editor/RichTextEditor';
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

// ── Keyboard trap (accessibility) ───────────────────────────────────────────
//
// Tab-through used to WRAP: when no ${field} remained ahead of the caret it selected the FIRST
// field in the document again. That made the handler return true unconditionally for any section
// whose text contained even one leftover ${...}, so Tab was swallowed forever and keyboard focus
// could never leave the editor going forward. A radiologist who expands a snippet and doesn't fill
// every blank — or who reopens a saved report still holding a ${...} — was trapped.
//
// Forward Tab must therefore be strictly forward: advance while fields remain ahead, then fall
// through to the browser's focus move. (Wrapping backwards is what Shift+Tab is for.)
describe('SectionEditor — Tab must never trap keyboard focus', () => {
  /** fireEvent returns false when the handler called preventDefault (i.e. Tab was swallowed). */
  function pressTab(el: Element): boolean {
    return fireEvent.keyDown(el, { key: 'Tab' });
  }

  it('falls through when a leftover ${field} sits BEHIND the caret', async () => {
    const onChange = vi.fn();
    render(<SectionEditor sectionKey="findings" value={'The ${finding} is present.'} onChange={onChange} />);
    await screen.findByRole('textbox');

    // Put the caret at the very end — past the only field in the document.
    act(() => {
      noteSectionEditorFocus('findings');
      getLastFocusedSectionEditor()?.insertAtCursor('No other abnormality.');
    });

    // Nothing ahead to jump to → Tab belongs to the browser, not to us.
    expect(pressTab(screen.getByRole('textbox'))).toBe(true);
  });

  it('advances through the fields ahead, then releases Tab', async () => {
    const onChange = vi.fn();
    render(<SectionEditor sectionKey="findings" value={'A ${size} nodule in the ${lobe}.'} onChange={onChange} />);
    const box = await screen.findByRole('textbox');

    // Caret starts at the top of the document, so both fields are ahead.
    expect(pressTab(box)).toBe(false); // → ${size}
    expect(pressTab(box)).toBe(false); // → ${lobe}
    expect(pressTab(box)).toBe(true); // nothing left ahead → release
  });
});

// ── The wizard's rich editors ───────────────────────────────────────────────
//
// RichTextEditor (the intake wizard's "clinical history" / "positive findings" fields) registered
// InterimDictation but not SnippetExpansion, so snippets silently did nothing on the one screen
// where a radiologist types the most free prose. Same feature, same keystroke, different editor.
describe('RichTextEditor — snippets work in the intake wizard too', () => {
  it('expands a trigger on Tab', async () => {
    saveSnippet({ trigger: 'nlchest', body: 'The lungs are clear.' });
    const onChange = vi.fn();
    render(<RichTextEditor sectionKey="intake-findings" onChange={onChange} />);
    await screen.findByRole('textbox');

    act(() => {
      noteSectionEditorFocus('intake-findings');
      getLastFocusedSectionEditor()?.insertAtCursor('nlchest');
    });

    fireEvent.keyDown(screen.getByRole('textbox'), { key: 'Tab' });

    await waitFor(() => {
      const last = onChange.mock.calls.at(-1)?.[0] as string | undefined;
      expect(last ?? '').toContain('The lungs are clear.');
    });
  });

  it('leaves Tab alone when there is no snippet context', async () => {
    const onChange = vi.fn();
    render(<RichTextEditor sectionKey="intake-history" onChange={onChange} />);
    const box = await screen.findByRole('textbox');

    act(() => {
      noteSectionEditorFocus('intake-history');
      getLastFocusedSectionEditor()?.insertAtCursor('Cough for three weeks.');
    });

    // No trigger, no fields — the wizard's Tab-to-next-control must still work.
    expect(fireEvent.keyDown(box, { key: 'Tab' })).toBe(true);
  });
});
