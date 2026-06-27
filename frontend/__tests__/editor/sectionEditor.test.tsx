import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, act } from '@testing-library/react';
import SectionEditor from '@/components/editor/SectionEditor';
import {
  getLastFocusedSectionEditor,
  noteSectionEditorFocus,
  _resetSectionEditorRegistry,
} from '@/lib/editor/sectionEditorRegistry';

beforeEach(() => _resetSectionEditorRegistry());

describe('SectionEditor', () => {
  it('renders the section value as text', async () => {
    render(<SectionEditor sectionKey="findings" value="No acute findings." onChange={vi.fn()} />);
    expect(await screen.findByText('No acute findings.')).toBeInTheDocument();
  });

  it('registers an imperative handle that the dictation overlay can target', async () => {
    render(<SectionEditor sectionKey="impression" value="" onChange={vi.fn()} />);
    // Wait for the editor (and its handle) to mount.
    await screen.findByRole('textbox');
    noteSectionEditorFocus('impression');
    const handle = getLastFocusedSectionEditor();
    expect(handle?.sectionKey).toBe('impression');
    expect(typeof handle?.insertAtCursor).toBe('function');
  });

  it('inserts dictated text with smart spacing at the caret', async () => {
    const onChange = vi.fn();
    render(<SectionEditor sectionKey="findings" value="The lung" onChange={onChange} />);
    await screen.findByRole('textbox');
    const handle = (() => {
      noteSectionEditorFocus('findings');
      return getLastFocusedSectionEditor();
    })();
    act(() => handle?.insertAtCursor('is clear'));
    // onChange fires with the merged plain text (smart-spaced).
    expect(onChange).toHaveBeenCalled();
    const last = onChange.mock.calls.at(-1)?.[0];
    expect(last).toContain('The lung is clear');
  });
});
