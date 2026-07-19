// F3 — in-editor snippet auto-expansion for the Tiptap section editor. Tab expands the trigger word
// sitting right before the caret into its snippet body, selecting the first ${field}; a subsequent
// Tab advances through the remaining fields. When there is no snippet context, Tab falls through to
// its default (focus move) so nothing is hijacked.
//
// All position math is done in PLAIN-TEXT space and mapped to ProseMirror positions via
// `plainOffsetToPmPos` (the same tested utility the correction decorations use), so it is robust to
// multi-line snippet bodies. The recognition logic is pure and unit-tested; the Tiptap application
// is thin.

import { Extension, type Editor } from '@tiptap/core';
import { docToString, plainOffsetToPmPos, stringToDoc } from '@/lib/editor/plainText';
import { findSnippetByTrigger, findFields, type Snippet } from '@/lib/snippets';

/** The whitespace-delimited word immediately before the caret (the candidate trigger). */
export function triggerWordBefore(textBeforeCaret: string): string {
  const m = /(\S+)$/.exec(textBeforeCaret);
  return m ? m[1] : '';
}

/** The snippet whose trigger is the word right before the caret, or null. Reads the local store. */
export function computeTriggerExpansion(
  textBeforeCaret: string,
): { word: string; snippet: Snippet } | null {
  const word = triggerWordBefore(textBeforeCaret);
  if (!word) return null;
  const snippet = findSnippetByTrigger(word);
  return snippet ? { word, snippet } : null;
}

/** Select the first ${field} at/after `plainFrom` (wrapping). Returns false when the doc has none. */
function selectFieldAtOrAfter(editor: Editor, plainFrom: number): boolean {
  const text = docToString(editor.getJSON());
  const fields = findFields(text);
  if (fields.length === 0) return false;
  const target = fields.find((f) => f.start >= plainFrom) ?? fields[0];
  const from = plainOffsetToPmPos(text, target.start);
  const to = plainOffsetToPmPos(text, target.end);
  editor.chain().setTextSelection({ from, to }).run();
  return true;
}

export const SnippetExpansion = Extension.create({
  name: 'rpSnippetExpansion',
  addKeyboardShortcuts() {
    return {
      Tab: ({ editor }) => {
        const sel = editor.state.selection;

        // 1) Expand a trigger word sitting immediately before an empty caret.
        if (sel.empty) {
          const beforeText = editor.state.doc.textBetween(0, sel.from, '\n', '\n');
          const match = computeTriggerExpansion(beforeText);
          if (match) {
            const wordStartPos = sel.from - match.word.length;
            const bodyStartOffset = beforeText.length - match.word.length;
            editor
              .chain()
              .focus()
              // Build the paragraphs explicitly rather than handing Tiptap a raw string.
              //
              // NOT a bug fix: passing the string directly was verified to preserve newlines here.
              // But that relies on Tiptap's string parsing incidentally doing the right thing,
              // which a version bump could change — and plainText.ts documents the opposite as the
              // default ("Tiptap's default string handling parses HTML and collapses newlines").
              // Every other insertion path in the editor already goes through stringToDoc; this was
              // the only one that did not. Making it explicit costs nothing and removes the
              // dependency on incidental behaviour. See the multi-line test in
              // __tests__/editor/snippetExpansion.test.tsx, which pins the guarantee either way.
              .insertContentAt({ from: wordStartPos, to: sel.from }, stringToDoc(match.snippet.body).content)
              .run();
            selectFieldAtOrAfter(editor, bodyStartOffset);
            return true;
          }
        }

        // 2) Tab-through: select the next ${field} after the current selection.
        const afterText = editor.state.doc.textBetween(0, sel.to, '\n', '\n');
        if (selectFieldAtOrAfter(editor, afterText.length)) return true;

        // 3) No snippet context — let the default Tab behaviour (focus move) happen.
        return false;
      },
    };
  },
});
