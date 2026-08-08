// Report Templates (REPORT-TEMPLATES) — converts between a Tiptap/ProseMirror doc
// (one paragraph per address line, marks carrying bold/italic/underline/font/size) and
// the letterhead's persisted `RichTextLine[]` model (schema.ts). Unlike the report
// intake's RichTextEditor (see lib/editor/docToMarkdown.ts), this is NOT lossy Markdown:
// font-family and point-size have no Markdown syntax, so the letterhead editor round-trips
// through this small structured serializer instead, and both directions are exercised —
// the intake editors are write-only (fresh each time), but a saved layout's letterhead
// must load back into the editor exactly as it was styled.

import type { JSONContent } from '@tiptap/core';
import type { LayoutFont, RichTextLine, RichTextRun } from './schema';

// Structurally compatible with Tiptap's `JSONContent` (all fields optional), matching
// the local shape docToMarkdown.ts already uses for the same reason (no @tiptap/core
// type import needed just to read/build plain JSON).
interface PmMark {
  type?: string;
  attrs?: Record<string, unknown> | null;
}
interface PmNode {
  type?: string;
  text?: string;
  marks?: PmMark[];
  content?: PmNode[];
}

const FONT_FAMILY_TO_ENUM: Record<string, LayoutFont> = {
  'sans-serif': 'sans',
  serif: 'serif',
  monospace: 'mono',
};

/** The CSS generic-family keyword each LayoutFont maps to for the FontFamily mark. */
export const ENUM_TO_FONT_FAMILY: Record<LayoutFont, string> = {
  sans: 'sans-serif',
  serif: 'serif',
  mono: 'monospace',
};

function marksToRun(text: string, marks: PmMark[] | undefined): RichTextRun {
  const run: RichTextRun = { text };
  for (const m of marks ?? []) {
    if (m.type === 'bold') run.bold = true;
    else if (m.type === 'italic') run.italic = true;
    else if (m.type === 'underline') run.underline = true;
    else if (m.type === 'textStyle') {
      const family = m.attrs?.fontFamily;
      if (typeof family === 'string' && FONT_FAMILY_TO_ENUM[family]) run.font = FONT_FAMILY_TO_ENUM[family];
      const size = m.attrs?.fontSize;
      if (typeof size === 'number' && Number.isFinite(size)) run.sizePt = size;
    }
  }
  return run;
}

/** Serialize a Tiptap doc (one paragraph per line) to the letterhead's `RichTextLine[]`. */
export function docToRichLines(doc: { content?: PmNode[] } | null | undefined): RichTextLine[] {
  const paragraphs = (doc?.content ?? []).filter((n) => n.type === 'paragraph');
  return paragraphs.map((p) => {
    const runs: RichTextRun[] = [];
    for (const child of p.content ?? []) {
      if (child.type === 'text' && child.text) runs.push(marksToRun(child.text, child.marks));
    }
    return { runs };
  });
}

function runMarks(run: RichTextRun): NonNullable<JSONContent['marks']> | undefined {
  const marks: NonNullable<JSONContent['marks']> = [];
  if (run.bold) marks.push({ type: 'bold' });
  if (run.italic) marks.push({ type: 'italic' });
  if (run.underline) marks.push({ type: 'underline' });
  if (run.font || run.sizePt !== undefined) {
    marks.push({
      type: 'textStyle',
      attrs: {
        fontFamily: run.font ? ENUM_TO_FONT_FAMILY[run.font] : null,
        fontSize: run.sizePt ?? null,
      },
    });
  }
  return marks.length > 0 ? marks : undefined;
}

/** Build the initial Tiptap doc JSON for editing an existing letterhead's `RichTextLine[]`. */
export function richLinesToDoc(lines: RichTextLine[]): JSONContent {
  if (lines.length === 0) {
    return { type: 'doc', content: [{ type: 'paragraph' }] };
  }
  return {
    type: 'doc',
    content: lines.map((line) => ({
      type: 'paragraph',
      content: line.runs
        .filter((r) => r.text.length > 0)
        .map((r) => ({ type: 'text', text: r.text, marks: runMarks(r) })),
    })),
  };
}
