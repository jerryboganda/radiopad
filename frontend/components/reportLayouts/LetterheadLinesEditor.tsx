'use client';

// Report Templates (REPORT-TEMPLATES) — a small Tiptap rich-text editor for the
// letterhead's "Address / extra lines" box. Each paragraph is one address line (capped
// at 4, matching the field's original plain-textarea limit); within a line, any
// selection can be independently bold/italic/underlined and given its own font family
// and point size — full per-run formatting, not a single block-level toggle (contrast
// with TextBlock's whole-block italic/fontSizeDelta flags elsewhere in this designer).
//
// Uncontrolled by design, matching RichTextEditor (components/editor/RichTextEditor.tsx):
// Tiptap owns the document after mount and emits the serialized RichTextLine[] via
// `onChange`. `lines` seeds the editor's INITIAL content only — resyncing it on every
// keystroke would fight the user's own edits and reset the cursor. LetterheadFields
// (InspectorPanel.tsx) already remounts this component fresh whenever the letterhead
// block is deselected and reselected, or a different saved layout loads, so a stale
// initial value is never an issue in practice.

import { useEditor, EditorContent } from '@tiptap/react';
import StarterKit from '@tiptap/starter-kit';
import Underline from '@tiptap/extension-underline';
import {
  Bold as BoldIcon,
  Italic as ItalicIcon,
  Underline as UnderlineIcon,
  Minus as MinusIcon,
  Plus as PlusIcon,
} from 'lucide-react';
import { LetterheadTextStyle, MIN_FONT_SIZE_PT, MAX_FONT_SIZE_PT } from '@/lib/editor/letterheadTextStyle';
import { docToRichLines, richLinesToDoc, ENUM_TO_FONT_FAMILY } from '@/lib/reportLayouts/richTextSerialize';
import type { RichTextLine } from '@/lib/reportLayouts/schema';
import { FONT_LABEL } from '@/lib/reportLayouts/accents';

/** Matches the fixed 9pt the renderers use for letterhead address lines (LayoutPaper.tsx,
 *  PdfLayoutRenderer.RenderLetterhead, DocxLayoutRenderer.AppendLetterhead) — the size the
 *  +/- stepper starts stepping from before the user has set an explicit size. */
const DEFAULT_LINE_SIZE_PT = 9;
const MAX_LINES = 4;

export interface LetterheadLinesEditorProps {
  lines: RichTextLine[];
  onChange: (lines: RichTextLine[]) => void;
}

export default function LetterheadLinesEditor({ lines, onChange }: LetterheadLinesEditorProps) {
  const editor = useEditor({
    immediatelyRender: false, // avoid Next SSR hydration mismatch
    extensions: [
      // Address lines are plain paragraphs — no headings/lists/blockquotes/code/rules,
      // and no soft line breaks (hardBreak off) so "one paragraph = one address line"
      // stays exact for the MAX_LINES cap and the RichTextLine[] round trip.
      StarterKit.configure({
        heading: false,
        bulletList: false,
        orderedList: false,
        listItem: false,
        blockquote: false,
        codeBlock: false,
        horizontalRule: false,
        hardBreak: false,
        strike: false,
        code: false,
      }),
      Underline,
      LetterheadTextStyle,
    ],
    content: richLinesToDoc(lines),
    editorProps: {
      attributes: {
        class: 'rp-rte-surface rp-rl-letterhead-lines',
        role: 'textbox',
        'aria-multiline': 'true',
        'aria-label': 'Address / extra lines',
      },
      // Blocks a 5th line — Enter still works to split/create paragraphs up to the cap,
      // it just stops doing anything once the doc already holds MAX_LINES of them.
      handleKeyDown: (view, event) => {
        if (event.key === 'Enter' && !event.shiftKey && view.state.doc.content.childCount >= MAX_LINES) {
          event.preventDefault();
          return true;
        }
        return false;
      },
    },
    onUpdate({ editor: e }) {
      onChange(docToRichLines(e.getJSON()));
    },
  });

  if (!editor) return null;

  const activeFamily = (editor.getAttributes('textStyle').fontFamily as string | null) ?? '';
  const activeSize = (editor.getAttributes('textStyle').fontSize as number | null) ?? DEFAULT_LINE_SIZE_PT;

  function setFamily(value: string) {
    if (value === '') {
      editor?.chain().focus().setLetterheadFontFamily(null).run();
    } else {
      editor?.chain().focus().setLetterheadFontFamily(value).run();
    }
  }

  function stepSize(delta: number) {
    const next = Math.max(MIN_FONT_SIZE_PT, Math.min(MAX_FONT_SIZE_PT, activeSize + delta));
    editor?.chain().focus().setLetterheadFontSize(next).run();
  }

  return (
    <div className="rp-rte">
      <div className="rp-rte-toolbar" role="toolbar" aria-label="Address text formatting">
        <ToolbarToggle
          label="Bold"
          active={editor.isActive('bold')}
          onClick={() => editor.chain().focus().toggleBold().run()}
        >
          <BoldIcon size={14} aria-hidden />
        </ToolbarToggle>
        <ToolbarToggle
          label="Italic"
          active={editor.isActive('italic')}
          onClick={() => editor.chain().focus().toggleItalic().run()}
        >
          <ItalicIcon size={14} aria-hidden />
        </ToolbarToggle>
        <ToolbarToggle
          label="Underline"
          active={editor.isActive('underline')}
          onClick={() => editor.chain().focus().toggleUnderline().run()}
        >
          <UnderlineIcon size={14} aria-hidden />
        </ToolbarToggle>
        <span className="rp-rte-divider" aria-hidden />
        <select
          aria-label="Font family"
          className="rp-rl-letterhead-font-select"
          value={activeFamily}
          onChange={(e) => setFamily(e.target.value)}
          onMouseDown={(e) => e.stopPropagation()}
        >
          <option value="">Default</option>
          <option value={ENUM_TO_FONT_FAMILY.sans}>{FONT_LABEL.sans}</option>
          <option value={ENUM_TO_FONT_FAMILY.serif}>{FONT_LABEL.serif}</option>
          <option value={ENUM_TO_FONT_FAMILY.mono}>{FONT_LABEL.mono}</option>
        </select>
        <span className="rp-rte-divider" aria-hidden />
        <ToolbarToggle
          label="Decrease font size"
          disabled={activeSize <= MIN_FONT_SIZE_PT}
          onClick={() => stepSize(-1)}
        >
          <MinusIcon size={13} aria-hidden />
        </ToolbarToggle>
        <span className="rp-rl-letterhead-size" aria-live="polite">{activeSize}pt</span>
        <ToolbarToggle
          label="Increase font size"
          disabled={activeSize >= MAX_FONT_SIZE_PT}
          onClick={() => stepSize(1)}
        >
          <PlusIcon size={13} aria-hidden />
        </ToolbarToggle>
      </div>
      <EditorContent editor={editor} />
    </div>
  );
}

function ToolbarToggle({
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
