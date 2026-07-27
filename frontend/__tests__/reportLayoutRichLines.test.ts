import { describe, it, expect } from 'vitest';
import { validateLayout, createEmptyLayout, type LetterheadBlock } from '@/lib/reportLayouts/schema';
import { docToRichLines, richLinesToDoc } from '@/lib/reportLayouts/richTextSerialize';

function baseLayout(lines: unknown) {
  const layout = createEmptyLayout();
  const letterhead = layout.blocks[0] as LetterheadBlock;
  return {
    ...layout,
    blocks: [{ ...letterhead, lines }, ...layout.blocks.slice(1)],
  };
}

describe('letterhead rich-text lines validation', () => {
  it('accepts legacy plain-string lines and wraps them as one unstyled run', () => {
    const result = validateLayout(baseLayout(['123 Radiology Way', '']));
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    const letterhead = result.value.blocks[0] as LetterheadBlock;
    expect(letterhead.lines).toEqual([
      { runs: [{ text: '123 Radiology Way' }] },
      { runs: [] },
    ]);
  });

  it('accepts rich-run lines and preserves bold/italic/underline/font/sizePt', () => {
    const lines = [
      {
        runs: [
          { text: 'Suite ', bold: true },
          { text: '400', italic: true, underline: true, font: 'serif', sizePt: 12 },
        ],
      },
    ];
    const result = validateLayout(baseLayout(lines));
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    const letterhead = result.value.blocks[0] as LetterheadBlock;
    expect(letterhead.lines).toEqual(lines);
  });

  it('rejects more than 4 lines', () => {
    const result = validateLayout(baseLayout(['a', 'b', 'c', 'd', 'e']));
    expect(result.ok).toBe(false);
  });

  it('rejects a line whose combined run text exceeds 120 characters', () => {
    const longRun = { text: 'x'.repeat(121) };
    const result = validateLayout(baseLayout([{ runs: [longRun] }]));
    expect(result.ok).toBe(false);
  });

  it('rejects an unrecognised run font', () => {
    const result = validateLayout(baseLayout([{ runs: [{ text: 'hi', font: 'comic-sans' }] }]));
    expect(result.ok).toBe(false);
  });

  it('rejects an out-of-range run sizePt', () => {
    const result = validateLayout(baseLayout([{ runs: [{ text: 'hi', sizePt: 40 }] }]));
    expect(result.ok).toBe(false);
  });
});

describe('richTextSerialize round trip', () => {
  it('converts RichTextLine[] to a Tiptap doc and back unchanged', () => {
    const lines = [
      { runs: [{ text: 'Bold ', bold: true }, { text: 'and italic', italic: true, font: 'mono' as const, sizePt: 10 }] },
      { runs: [{ text: 'Plain second line' }] },
    ];
    const doc = richLinesToDoc(lines);
    expect(docToRichLines(doc)).toEqual(lines);
  });

  it('represents an empty lines array as a single empty paragraph, round-tripping to one empty line', () => {
    const doc = richLinesToDoc([]);
    expect(doc.content).toHaveLength(1);
    expect(docToRichLines(doc)).toEqual([{ runs: [] }]);
  });
});
