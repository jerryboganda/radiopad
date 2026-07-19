// F3 — in-editor snippet auto-expansion for the Tiptap editors. Tab expands the trigger word sitting
// right before the caret into its snippet body, selecting the first ${field}; a subsequent Tab
// advances through the remaining fields. When there is no snippet context, Tab falls through to its
// default (focus move) so nothing is hijacked.
//
// Field positions are read straight off the ProseMirror document rather than via the plain-text
// projection. `plainText.docToString` only walks doc → paragraph → text (that is all the section
// editor's schema holds), so on the intake wizard's StarterKit schema it returns '' for anything
// nested inside a list item and every derived offset would be wrong. Scanning text nodes with
// `descendants` is schema-agnostic and gives absolute PM positions directly — no mapping step, and
// correct in both editors. (A ${field} split across two text nodes by a mark boundary is not matched;
// that would require the placeholder to be half-bolded, and missing it degrades to "Tab moves focus"
// rather than to a wrong position.)

import { Extension, type Editor } from '@tiptap/core';
import type { Node as PmNode } from '@tiptap/pm/model';
import { stringToDoc } from '@/lib/editor/plainText';
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

export interface PmFieldRange {
  from: number;
  to: number;
}

/** Every ${field} placeholder in the document, as absolute PM positions, in document order. */
export function fieldRangesInDoc(doc: PmNode): PmFieldRange[] {
  const out: PmFieldRange[] = [];
  doc.descendants((node, pos) => {
    if (!node.isText || !node.text) return;
    // Reuse the tested placeholder regex; offsets are relative to this text node.
    for (const f of findFields(node.text)) out.push({ from: pos + f.start, to: pos + f.end });
  });
  return out;
}

/**
 * The next ${field} starting at/after `fromPos`, or null when none remain ahead.
 *
 * Deliberately does NOT wrap to the first field. Wrapping made the Tab handler return "handled"
 * for any document containing a single leftover ${...}, which swallowed Tab forever and trapped
 * keyboard focus inside the editor — see the accessibility tests in
 * __tests__/editor/snippetExpansion.test.tsx. Forward Tab advances while there is somewhere ahead
 * to go and then releases the key to the browser; Shift+Tab remains the way back.
 */
export function nextFieldAtOrAfter(doc: PmNode, fromPos: number): PmFieldRange | null {
  return fieldRangesInDoc(doc).find((f) => f.from >= fromPos) ?? null;
}

/** Select the next field at/after `fromPos`. Returns false when there is none (Tab falls through). */
function selectNextField(editor: Editor, fromPos: number): boolean {
  const target = nextFieldAtOrAfter(editor.state.doc, fromPos);
  if (!target) return false;
  editor.chain().setTextSelection({ from: target.from, to: target.to }).run();
  return true;
}

export const SnippetExpansion = Extension.create({
  name: 'rpSnippetExpansion',
  // Ask us before the schema's own Tab bindings (StarterKit binds Tab to list indent/outdent in the
  // intake wizard). We return false whenever there is no snippet context, so list indenting still
  // works everywhere a snippet is not being expanded.
  priority: 1000,
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
            editor
              .chain()
              .focus()
              // Build the paragraphs explicitly rather than handing Tiptap a raw string, so a
              // multi-line canned block keeps its lines regardless of Tiptap's string parsing.
              .insertContentAt({ from: wordStartPos, to: sel.from }, stringToDoc(match.snippet.body).content)
              .run();
            // Every field of the freshly inserted body starts at/after where it was inserted.
            selectNextField(editor, wordStartPos);
            return true;
          }
        }

        // 2) Tab-through: select the next ${field} ahead of the current selection.
        if (selectNextField(editor, sel.to)) return true;

        // 3) No snippet context — let the default Tab behaviour (focus move) happen.
        return false;
      },
    };
  },
});
