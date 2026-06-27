// Plain-text <-> ProseMirror doc mapping for the report section editors.
//
// Report sections are stored — in the backend, in PATCH payloads, and in the
// Tauri offline-draft store — as plain strings with '\n' newlines. Tiptap's
// default string handling parses HTML and collapses newlines, so we map each
// line to its own paragraph and serialize back with a single-'\n' block
// separator. This round-trips EXACTLY, keeping the storage contract unchanged
// while letting the editor render rich inline correction highlights.

export interface PmTextNode {
  type: 'text';
  text: string;
}
export interface PmParagraph {
  type: 'paragraph';
  content?: PmTextNode[];
}
export interface PmDoc {
  type: 'doc';
  content: PmParagraph[];
}

/** Build a ProseMirror doc (one paragraph per line) from a plain string. */
export function stringToDoc(value: string): PmDoc {
  const lines = (value ?? '').split('\n');
  return {
    type: 'doc',
    content: lines.map((line) =>
      line.length > 0
        ? { type: 'paragraph', content: [{ type: 'text', text: line }] }
        : { type: 'paragraph' },
    ),
  };
}

/** Serialize a ProseMirror doc JSON back to the plain '\n'-joined string. */
export function docToString(
  doc: { content?: Array<{ content?: Array<{ text?: string }> }> } | null | undefined,
): string {
  const paras = doc?.content ?? [];
  return paras
    .map((p) => (p.content ?? []).map((n) => n.text ?? '').join(''))
    .join('\n');
}

/**
 * Map a character offset in the plain section string to an absolute
 * ProseMirror position. PM inserts an open+close token (2 positions) at each
 * paragraph boundary where the plain text has a single '\n' (1 char), so the
 * mapping is not a simple offset+1. Used to anchor correction-highlight
 * decorations. Clamps to [0, value.length].
 */
export function plainOffsetToPmPos(value: string, offset: number): number {
  const text = value ?? '';
  const clamped = Math.max(0, Math.min(offset, text.length));
  const lines = text.split('\n');
  let plainPos = 0;
  let pmContentStart = 1; // first cursor position inside the first paragraph
  for (let i = 0; i < lines.length; i++) {
    const lineLen = lines[i].length;
    if (clamped <= plainPos + lineLen) {
      return pmContentStart + (clamped - plainPos);
    }
    plainPos += lineLen + 1; // skip the line + its trailing '\n'
    pmContentStart += lineLen + 2; // skip the line + paragraph close/open tokens
  }
  const lastLen = lines.length ? lines[lines.length - 1].length : 0;
  return pmContentStart + lastLen;
}
