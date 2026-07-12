'use client';

// Rich (Tiptap/ProseMirror) editor for a single report section. Drop-in for the
// plain <textarea> it replaces: it serializes to/from the exact same plain
// string (see lib/editor/plainText), so the backend + offline-draft storage
// contract is unchanged. Its reason to exist is non-destructive inline
// correction highlights (cross-check), rendered as ProseMirror decorations that
// never mutate the stored content.

import { useEffect, useRef } from 'react';
import { useEditor, EditorContent } from '@tiptap/react';
import { Extension } from '@tiptap/core';
import { Plugin, PluginKey } from '@tiptap/pm/state';
import { Decoration, DecorationSet } from '@tiptap/pm/view';
import Document from '@tiptap/extension-document';
import Paragraph from '@tiptap/extension-paragraph';
import Text from '@tiptap/extension-text';
import History from '@tiptap/extension-history';
import { stringToDoc, docToString, plainOffsetToPmPos } from '@/lib/editor/plainText';
import { withSmartSpacing } from '@/lib/dictation/insertIntoEditor';
import {
  registerSectionEditor,
  unregisterSectionEditor,
  noteSectionEditorFocus,
  type SectionEditorHandle,
} from '@/lib/editor/sectionEditorRegistry';
import type { EditorCorrection } from '@/lib/editor/corrections';
import { clinicalLineRole } from '@/lib/editor/clinicalStructure';

const correctionPluginKey = new PluginKey<DecorationSet>('rpCorrections');

const ClinicalStructure = Extension.create({
  name: 'rpClinicalStructure',
  addProseMirrorPlugins() {
    return [
      new Plugin({
        props: {
          decorations(state) {
            const decorations: Decoration[] = [];
            state.doc.descendants((node, pos) => {
              if (node.type.name !== 'paragraph') return;
              const role = clinicalLineRole(node.textContent);
              if (role === 'body') return;
              decorations.push(Decoration.node(pos, pos + node.nodeSize, {
                class: `rp-clinical-${role}`,
              }));
            });
            return DecorationSet.create(state.doc, decorations);
          },
        },
      }),
    ];
  },
});

// Holds the ephemeral correction-highlight DecorationSet. Decorations are pushed
// in via a meta transaction; on every other transaction they are remapped
// through the change so they track edits, never persisted to the document.
const CorrectionHighlight = Extension.create({
  name: 'rpCorrectionHighlight',
  addProseMirrorPlugins() {
    return [
      new Plugin<DecorationSet>({
        key: correctionPluginKey,
        state: {
          init: () => DecorationSet.empty,
          apply(tr, old) {
            const meta = tr.getMeta(correctionPluginKey) as { decos: Decoration[] } | undefined;
            if (meta) return DecorationSet.create(tr.doc, meta.decos);
            return old.map(tr.mapping, tr.doc);
          },
        },
        props: {
          decorations(state) {
            return correctionPluginKey.getState(state);
          },
        },
      }),
    ];
  },
});

export interface SectionEditorProps {
  sectionKey: string;
  value: string;
  className?: string;
  ariaLabel?: string;
  corrections?: EditorCorrection[];
  onChange: (value: string) => void;
  onBlur?: (value: string) => void;
  onCorrectionClick?: (id: string) => void;
}

export default function SectionEditor({
  sectionKey,
  value,
  className,
  ariaLabel,
  corrections,
  onChange,
  onBlur,
  onCorrectionClick,
}: SectionEditorProps) {
  // Stable refs so the editor is created once but always calls the latest props.
  const onChangeRef = useRef(onChange);
  onChangeRef.current = onChange;
  const onBlurRef = useRef(onBlur);
  onBlurRef.current = onBlur;
  const onCorrectionClickRef = useRef(onCorrectionClick);
  onCorrectionClickRef.current = onCorrectionClick;

  const editor = useEditor({
    immediatelyRender: false, // avoid Next SSR hydration mismatch
    extensions: [Document, Paragraph, Text, History, ClinicalStructure, CorrectionHighlight],
    content: stringToDoc(value),
    editorProps: {
      attributes: {
        class: `rp-section-editor${className ? ` ${className}` : ''}`,
        role: 'textbox',
        'aria-multiline': 'true',
        ...(ariaLabel ? { 'aria-label': ariaLabel } : {}),
      },
      handleClickOn(_view, _pos, _node, _nodePos, event) {
        const el = (event.target as HTMLElement | null)?.closest('[data-correction-id]');
        const id = el?.getAttribute('data-correction-id');
        if (id && onCorrectionClickRef.current) {
          onCorrectionClickRef.current(id);
          return true;
        }
        return false;
      },
    },
    onUpdate({ editor }) {
      onChangeRef.current(docToString(editor.getJSON()));
    },
    onBlur({ editor }) {
      onBlurRef.current?.(docToString(editor.getJSON()));
    },
    onFocus() {
      noteSectionEditorFocus(sectionKey);
    },
  });

  // Register/unregister the imperative handle the dictation overlay targets.
  useEffect(() => {
    if (!editor) return;
    const handle: SectionEditorHandle = {
      sectionKey,
      focus: () => editor.commands.focus(),
      insertAtCursor: (text: string) => {
        // Match the textarea's `selectionStart ?? value.length` default: when the
        // editor has no active caret (dictation invoked while focus is on the mic
        // button or after an async transcribe), append at the end.
        if (!editor.isFocused) editor.commands.focus('end');
        const { from } = editor.state.selection;
        const before = editor.state.doc.textBetween(0, from, '\n', '\n');
        const insert = withSmartSpacing(before, text);
        editor.chain().focus().insertContent(insert).run();
      },
    };
    registerSectionEditor(handle);
    return () => unregisterSectionEditor(sectionKey);
  }, [editor, sectionKey]);

  // External value sync (initial load, AI generate/rewrite/cleanup, accept
  // correction). Guarded against the round-trip from our own onChange so typing
  // doesn't reset the document mid-keystroke.
  useEffect(() => {
    if (!editor) return;
    if (docToString(editor.getJSON()) !== value) {
      editor.commands.setContent(stringToDoc(value), false);
    }
  }, [editor, value]);

  // Project corrections onto the document as inline decorations.
  useEffect(() => {
    if (!editor) return;
    const text = docToString(editor.getJSON());
    const decos = (corrections ?? []).map((c) =>
      Decoration.inline(
        plainOffsetToPmPos(text, c.startOffset),
        plainOffsetToPmPos(text, c.endOffset),
        { class: `rp-xc-correction ${c.severity}`, 'data-correction-id': c.id },
      ),
    );
    editor.view.dispatch(editor.state.tr.setMeta(correctionPluginKey, { decos }));
  }, [editor, corrections, value]);

  return <EditorContent editor={editor} data-section-editor={sectionKey} />;
}
