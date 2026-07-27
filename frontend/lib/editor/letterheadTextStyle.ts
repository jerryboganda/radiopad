// A single custom Tiptap mark carrying BOTH per-run font-family and font-size for the
// Report Templates letterhead address-lines editor (LetterheadLinesEditor) — the one place
// in the app that needs per-selection font control, unlike the report intake's
// RichTextEditor (docToMarkdown.ts), which stays plain-text with no font concept at all.
//
// Tiptap ships an official `@tiptap/extension-font-family`, but it and a hand-rolled
// font-size extension would BOTH extend the shared `textStyle` mark under the same
// registered name — Tiptap's extension manager keeps only the last one with a given
// name, silently discarding the other's attributes. So both attributes (and both sets of
// commands) live on this one extension instead of two competing ones.

import TextStyle from '@tiptap/extension-text-style';

export const MIN_FONT_SIZE_PT = 6;
export const MAX_FONT_SIZE_PT = 24;

declare module '@tiptap/core' {
  interface Commands<ReturnType> {
    letterheadTextStyle: {
      setLetterheadFontFamily: (family: string | null) => ReturnType;
      setLetterheadFontSize: (sizePt: number | null) => ReturnType;
    };
  }
}

export const LetterheadTextStyle = TextStyle.extend({
  addAttributes() {
    return {
      ...this.parent?.(),
      fontFamily: {
        default: null,
        parseHTML: (element) => element.style.fontFamily || null,
        renderHTML: (attributes) => {
          if (!attributes.fontFamily) return {};
          return { style: `font-family: ${attributes.fontFamily}` };
        },
      },
      fontSize: {
        default: null,
        parseHTML: (element) => {
          const value = element.style.fontSize;
          return value ? Number.parseFloat(value) || null : null;
        },
        renderHTML: (attributes) => {
          if (!attributes.fontSize) return {};
          return { style: `font-size: ${attributes.fontSize}pt` };
        },
      },
    };
  },

  addCommands() {
    return {
      setLetterheadFontFamily:
        (family: string | null) =>
        ({ chain }) =>
          chain().setMark('textStyle', { fontFamily: family }).run(),
      setLetterheadFontSize:
        (sizePt: number | null) =>
        ({ chain }) => {
          const clamped = sizePt === null
            ? null
            : Math.max(MIN_FONT_SIZE_PT, Math.min(MAX_FONT_SIZE_PT, Math.round(sizePt)));
          return chain().setMark('textStyle', { fontSize: clamped }).run();
        },
    };
  },
});
