// ProseMirror/Tiptap doc JSON -> clean Markdown-ish plain text.
//
// The report intake's rich editors (RichTextEditor) let the radiologist author
// positive findings / clinical history with bold, italics, bullet + numbered
// lists and headings. Storage + the AI prompt, however, stay plain text (report
// sections are '\n'-joined strings — see lib/editor/plainText). This serializer
// flattens the rich doc to readable Markdown so the formatting survives as text
// the model can parse ("- item", "1. item", "**bold**", "## heading") without
// introducing any HTML or a schema change.

// Structurally compatible with Tiptap's `JSONContent` (all fields optional) so
// `editor.getJSON()` can be passed straight in.
interface PmMark {
  type?: string;
  [k: string]: unknown;
}
interface PmNode {
  type?: string;
  text?: string;
  attrs?: Record<string, unknown> | null;
  marks?: PmMark[];
  content?: PmNode[];
  [k: string]: unknown;
}

/** Wrap inline text in the Markdown tokens for its active marks. */
function applyMarks(text: string, marks?: PmMark[]): string {
  if (!text || !marks?.length) return text;
  let out = text;
  for (const m of marks) {
    if (m.type === 'bold') out = `**${out}**`;
    else if (m.type === 'italic') out = `*${out}*`;
    else if (m.type === 'strike') out = `~~${out}~~`;
    else if (m.type === 'code') out = `\`${out}\``;
  }
  return out;
}

/** Serialize a node's inline children (text + hard breaks) to a single line. */
function inlineText(node: PmNode): string {
  const kids = node.content ?? [];
  let out = '';
  for (const child of kids) {
    if (child.type === 'text') out += applyMarks(child.text ?? '', child.marks);
    else if (child.type === 'hardBreak') out += '\n';
    else out += inlineText(child); // defensive: unknown inline wrapper
  }
  return out;
}

/** Serialize the items of a bullet/ordered list, prefixing each with a marker. */
function serializeList(node: PmNode, ordered: boolean): string {
  const items = node.content ?? [];
  const lines: string[] = [];
  items.forEach((item, i) => {
    // A listItem holds block content (usually one paragraph); join its blocks
    // onto the single bullet line.
    const inner = (item.content ?? [])
      .map((b) => (b.type === 'paragraph' ? inlineText(b) : serializeBlock(b)))
      .filter((s) => s.length > 0)
      .join(' ');
    const marker = ordered ? `${i + 1}. ` : '- ';
    lines.push(marker + inner);
  });
  return lines.join('\n');
}

/** Serialize a single block-level node to its plain-text/Markdown form. */
function serializeBlock(node: PmNode): string {
  switch (node.type) {
    case 'paragraph':
      return inlineText(node);
    case 'heading': {
      const level = Math.min(6, Math.max(1, Number(node.attrs?.level ?? 2)));
      return `${'#'.repeat(level)} ${inlineText(node)}`;
    }
    case 'bulletList':
      return serializeList(node, false);
    case 'orderedList':
      return serializeList(node, true);
    case 'blockquote':
      return (node.content ?? [])
        .map((b) => serializeBlock(b))
        .join('\n')
        .split('\n')
        .map((l) => `> ${l}`)
        .join('\n');
    case 'codeBlock':
      return '```\n' + inlineText(node) + '\n```';
    case 'horizontalRule':
      return '---';
    default:
      // Unknown block: recurse if it has block content, else treat as inline.
      return (node.content ?? []).map((b) => serializeBlock(b)).join('\n\n') || inlineText(node);
  }
}

/**
 * Serialize a Tiptap/ProseMirror doc JSON to clean Markdown-ish plain text.
 * Blocks are separated by a blank line; empty leading/trailing whitespace is
 * trimmed. A doc with only an empty paragraph serializes to `''`.
 */
export function docToMarkdown(
  doc: { type?: string; content?: PmNode[] } | null | undefined,
): string {
  const blocks = doc?.content ?? [];
  return blocks
    .map((b) => serializeBlock(b))
    .join('\n\n')
    .replace(/\n{3,}/g, '\n\n')
    .trim();
}
