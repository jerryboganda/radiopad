'use client';

// Rich (Tiptap/ProseMirror) editor WITH a formatting toolbar — bold, italic,
// bullet + numbered lists, headings, undo/redo. Used by the report intake wizard
// (`/reports/new`) for the "positive findings" and "clinical history" fields so
// the radiologist can compose structured notes. It is UNCONTROLLED: Tiptap owns
// the document and emits clean Markdown-ish plain text via `onChange`
// (see lib/editor/docToMarkdown) — the value stored + sent to the AI stays plain
// text, no HTML, no schema change. It self-registers with the global
// DictationOverlay (via sectionEditorRegistry) exactly like SectionEditor, so the
// floating Dictate / HQ mic inserts transcribed text at the caret.

import { useEffect } from 'react';
import { useEditor, EditorContent } from '@tiptap/react';
import StarterKit from '@tiptap/starter-kit';
import {
  Bold as BoldIcon,
  Italic as ItalicIcon,
  List as ListIcon,
  ListOrdered as ListOrderedIcon,
  Heading2 as HeadingIcon,
  Undo2 as UndoIcon,
  Redo2 as RedoIcon,
} from 'lucide-react';
import { docToMarkdown } from '@/lib/editor/docToMarkdown';
import { withSmartSpacing } from '@/lib/dictation/insertIntoEditor';
import {
  InterimDictation,
  setInterimText,
  clearInterimText,
} from '@/lib/editor/interimDecoration';
import {
  registerSectionEditor,
  unregisterSectionEditor,
  noteSectionEditorFocus,
  type SectionEditorHandle,
} from '@/lib/editor/sectionEditorRegistry';
import { SnippetExpansion } from '@/lib/editor/snippetExpansion';

export interface RichTextEditorProps {
  /** Unique key for dictation focus tracking (e.g. "intake-findings"). */
  sectionKey: string;
  ariaLabel?: string;
  /** Extra class on the editable surface (e.g. a min-height variant). */
  className?: string;
  /** Called on every edit with the serialized Markdown-ish plain text. */
  onChange: (value: string) => void;
}

export default function RichTextEditor({
  sectionKey,
  ariaLabel,
  className,
  onChange,
}: RichTextEditorProps) {
  const editor = useEditor({
    immediatelyRender: false, // avoid Next SSR hydration mismatch
    extensions: [
      StarterKit.configure({
        heading: { levels: [2, 3] },
      }),
      InterimDictation,
      // Snippets are a per-user F3 feature, not a per-editor one: the wizard's free-prose fields
      // are exactly where canned blocks earn their keep, and this editor was silently the one
      // place they did nothing. SnippetExpansion outranks StarterKit's list-indent Tab binding but
      // yields whenever there is no trigger or field to act on, so Tab still indents inside lists
      // and still moves focus everywhere else.
      SnippetExpansion,
    ],
    content: '',
    editorProps: {
      attributes: {
        class: `rp-section-editor rp-rte-surface${className ? ` ${className}` : ''}`,
        role: 'textbox',
        'aria-multiline': 'true',
        ...(ariaLabel ? { 'aria-label': ariaLabel } : {}),
      },
    },
    onUpdate({ editor }) {
      onChange(docToMarkdown(editor.getJSON()));
    },
    onFocus() {
      noteSectionEditorFocus(sectionKey);
    },
  });

  // Register/unregister the imperative handle the dictation overlay targets, so a
  // dictated utterance lands at the caret of whichever rich field was last focused.
  useEffect(() => {
    if (!editor) return;
    const handle: SectionEditorHandle = {
      sectionKey,
      focus: () => editor.commands.focus(),
      insertAtCursor: (text: string) => {
        if (!editor.isFocused) editor.commands.focus('end');
        const { from } = editor.state.selection;
        const before = editor.state.doc.textBetween(0, from, '\n', '\n');
        const insert = withSmartSpacing(before, text);
        editor.chain().focus().insertContent(insert).run();
      },
      setInterim: (text: string) => setInterimText(editor, text),
      clearInterim: () => clearInterimText(editor),
      newLine: () => editor.chain().focus().splitBlock().run(),
      undo: () => editor.chain().focus().undo().run(),
    };
    registerSectionEditor(handle);
    return () => unregisterSectionEditor(sectionKey);
  }, [editor, sectionKey]);

  return (
    <div className="rp-rte">
      <div className="rp-rte-toolbar" role="toolbar" aria-label="Text formatting">
        <ToolbarButton
          label="Bold"
          active={!!editor?.isActive('bold')}
          disabled={!editor}
          onClick={() => editor?.chain().focus().toggleBold().run()}
        >
          <BoldIcon size={15} aria-hidden />
        </ToolbarButton>
        <ToolbarButton
          label="Italic"
          active={!!editor?.isActive('italic')}
          disabled={!editor}
          onClick={() => editor?.chain().focus().toggleItalic().run()}
        >
          <ItalicIcon size={15} aria-hidden />
        </ToolbarButton>
        <ToolbarButton
          label="Heading"
          active={!!editor?.isActive('heading', { level: 2 })}
          disabled={!editor}
          onClick={() => editor?.chain().focus().toggleHeading({ level: 2 }).run()}
        >
          <HeadingIcon size={15} aria-hidden />
        </ToolbarButton>
        <span className="rp-rte-divider" aria-hidden />
        <ToolbarButton
          label="Bullet list"
          active={!!editor?.isActive('bulletList')}
          disabled={!editor}
          onClick={() => editor?.chain().focus().toggleBulletList().run()}
        >
          <ListIcon size={15} aria-hidden />
        </ToolbarButton>
        <ToolbarButton
          label="Numbered list"
          active={!!editor?.isActive('orderedList')}
          disabled={!editor}
          onClick={() => editor?.chain().focus().toggleOrderedList().run()}
        >
          <ListOrderedIcon size={15} aria-hidden />
        </ToolbarButton>
        <span className="rp-rte-divider" aria-hidden />
        <ToolbarButton
          label="Undo"
          disabled={!editor?.can().undo()}
          onClick={() => editor?.chain().focus().undo().run()}
        >
          <UndoIcon size={15} aria-hidden />
        </ToolbarButton>
        <ToolbarButton
          label="Redo"
          disabled={!editor?.can().redo()}
          onClick={() => editor?.chain().focus().redo().run()}
        >
          <RedoIcon size={15} aria-hidden />
        </ToolbarButton>
      </div>
      <EditorContent editor={editor} data-section-editor={sectionKey} />
    </div>
  );
}

function ToolbarButton({
  label,
  active,
  disabled,
  onClick,
  children,
}: {
  label: string;
  active?: boolean;
  disabled?: boolean;
  onClick: () => void;
  children: React.ReactNode;
}) {
  return (
    <button
      type="button"
      className={`rp-rte-btn${active ? ' is-active' : ''}`}
      aria-label={label}
      aria-pressed={active || undefined}
      title={label}
      disabled={disabled}
      // Keep the editor selection: prevent the button from stealing focus.
      onMouseDown={(e) => e.preventDefault()}
      onClick={onClick}
    >
      {children}
    </button>
  );
}
