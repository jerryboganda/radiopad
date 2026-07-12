import { describe, it, expect } from 'vitest';
import { Editor } from '@tiptap/react';
import Document from '@tiptap/extension-document';
import Paragraph from '@tiptap/extension-paragraph';
import Text from '@tiptap/extension-text';
import History from '@tiptap/extension-history';
import {
  InterimDictation,
  interimDictationKey,
  setInterimText,
  clearInterimText,
} from '@/lib/editor/interimDecoration';
import { stringToDoc, docToString } from '@/lib/editor/plainText';

function makeEditor(value: string) {
  return new Editor({
    extensions: [Document, Paragraph, Text, History, InterimDictation],
    content: stringToDoc(value),
  });
}

describe('interim dictation decoration', () => {
  it('previews interim text WITHOUT changing the saved document', () => {
    const editor = makeEditor('Lungs are clear.');
    setInterimText(editor, 'no effusion');

    // The preview lives only in plugin state (a widget decoration) …
    expect(interimDictationKey.getState(editor.state)?.text).toContain('no effusion');
    // … never in the document the report saves.
    expect(docToString(editor.getJSON())).toBe('Lungs are clear.');

    clearInterimText(editor);
    expect(interimDictationKey.getState(editor.state)?.text).toBe('');
    expect(docToString(editor.getJSON())).toBe('Lungs are clear.');
    editor.destroy();
  });

  it('clears on empty and does not leave a stale anchor', () => {
    const editor = makeEditor('Impression:');
    setInterimText(editor, 'stable');
    expect(interimDictationKey.getState(editor.state)?.pos).not.toBeNull();
    setInterimText(editor, '');
    expect(interimDictationKey.getState(editor.state)?.text).toBe('');
    expect(interimDictationKey.getState(editor.state)?.pos).toBeNull();
    editor.destroy();
  });

  it('updates the RENDERED preview across partials (no stale-widget freeze)', () => {
    // Guards the ProseMirror widget-key regression: a constant key made PM reuse
    // the first partial's DOM node forever, so the live preview froze. The key now
    // encodes the text, so each partial re-renders.
    const mount = document.createElement('div');
    document.body.appendChild(mount);
    const editor = new Editor({
      element: mount,
      extensions: [Document, Paragraph, Text, History, InterimDictation],
      content: stringToDoc('Findings:'),
    });

    setInterimText(editor, 'chest');
    expect(mount.querySelector('.rp-interim-dictation')?.textContent).toContain('chest');

    setInterimText(editor, 'chest is clear');
    const span = mount.querySelector('.rp-interim-dictation');
    expect(span?.textContent).toContain('clear'); // NOT frozen on the first partial

    clearInterimText(editor);
    expect(mount.querySelector('.rp-interim-dictation')).toBeNull();

    editor.destroy();
    mount.remove();
  });

  it('a real dictated insert still commits to the document', () => {
    const editor = makeEditor('Findings:');
    setInterimText(editor, 'pending words');
    editor.chain().focus('end').insertContent(' no acute abnormality').run();
    clearInterimText(editor);
    expect(docToString(editor.getJSON())).toContain('no acute abnormality');
    expect(interimDictationKey.getState(editor.state)?.text).toBe('');
    editor.destroy();
  });
});
