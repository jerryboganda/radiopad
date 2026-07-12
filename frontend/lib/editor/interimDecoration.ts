// Real-time (interim) dictation preview for the report editors.
//
// When the phone companion streams partial speech results, the desktop shows the
// not-yet-final words live at the caret so the radiologist sees dictation appear
// in real time. The interim text is rendered as a ProseMirror WIDGET decoration —
// it is NEVER part of the document, so it never hits `onChange`, the saved value,
// the undo history, or validation. On a final result the caller clears the widget
// and inserts the committed text normally. Mirrors the ephemeral-decoration
// pattern already used for cross-check correction highlights.

import { Extension } from '@tiptap/core';
import { Plugin, PluginKey } from '@tiptap/pm/state';
import { Decoration, DecorationSet } from '@tiptap/pm/view';
import type { Editor } from '@tiptap/react';

export const interimDictationKey = new PluginKey<InterimState>('rpInterimDictation');

interface InterimState {
  /** Text to preview (already includes any leading space), or '' when idle. */
  text: string;
  /** Doc position to anchor the preview at, or null when idle. */
  pos: number | null;
}

export const InterimDictation = Extension.create({
  name: 'rpInterimDictation',
  addProseMirrorPlugins() {
    return [
      new Plugin<InterimState>({
        key: interimDictationKey,
        state: {
          init: () => ({ text: '', pos: null }),
          apply(tr, old) {
            const meta = tr.getMeta(interimDictationKey) as InterimState | undefined;
            if (meta) return meta;
            if (old.pos == null || !tr.docChanged) return old;
            return { text: old.text, pos: tr.mapping.map(old.pos) };
          },
        },
        props: {
          decorations(state) {
            const s = interimDictationKey.getState(state);
            if (!s || !s.text) return DecorationSet.empty;
            const pos = s.pos == null
              ? state.doc.content.size
              : Math.min(Math.max(s.pos, 0), state.doc.content.size);
            const widget = Decoration.widget(
              pos,
              () => {
                const span = document.createElement('span');
                span.className = 'rp-interim-dictation';
                span.textContent = s.text;
                return span;
              },
              // The key MUST vary with the text: ProseMirror reuses the existing
              // DOM for a widget whose key is unchanged (never re-calling the
              // factory), which would freeze the preview at the first partial.
              // Encoding the text forces a fresh widget on every update.
              { side: 1, ignoreSelection: true, key: `rp-interim:${s.text}` },
            );
            return DecorationSet.create(state.doc, [widget]);
          },
        },
      }),
    ];
  },
});

/** Show/replace the interim preview at the current caret (or doc end if unfocused). */
export function setInterimText(editor: Editor, text: string): void {
  const clean = (text ?? '').trim();
  const pos = editor.isFocused ? editor.state.selection.from : editor.state.doc.content.size;
  let display = clean;
  if (clean) {
    const before = editor.state.doc.textBetween(0, pos, '\n', '\n');
    if (before.length > 0 && !/\s$/.test(before)) display = ` ${clean}`;
  }
  editor.view.dispatch(
    editor.state.tr.setMeta(interimDictationKey, { text: display, pos: clean ? pos : null }),
  );
}

/** Remove the interim preview (call on a final result or when dictation stops). */
export function clearInterimText(editor: Editor): void {
  editor.view.dispatch(editor.state.tr.setMeta(interimDictationKey, { text: '', pos: null }));
}
