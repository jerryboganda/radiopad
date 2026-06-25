// Insert dictated text at the caret of a textarea/input. The tricky part is
// React: its <textarea value={...}> is a controlled input whose value is
// tracked internally, so assigning `el.value` directly is silently reverted on
// the next render. The fix is to call the *native* value setter (which updates
// React's value tracker) and then dispatch a bubbling `input` event so React's
// synthetic onChange fires and component state updates. This keeps the existing
// onChange -> setReport / onBlur -> PATCH wiring in ReportClient intact.

type EditableEl = HTMLTextAreaElement | HTMLInputElement;

function setNativeValue(el: EditableEl, value: string): void {
  const proto = el instanceof HTMLTextAreaElement
    ? HTMLTextAreaElement.prototype
    : HTMLInputElement.prototype;
  const descriptor = Object.getOwnPropertyDescriptor(proto, 'value');
  if (descriptor?.set) {
    descriptor.set.call(el, value);
  } else {
    el.value = value;
  }
}

export interface InsertOptions {
  /** Add a single separating space when appending mid-prose. Default true. */
  smartSpacing?: boolean;
}

export function insertAtCursor(el: EditableEl, text: string, opts: InsertOptions = {}): void {
  const { smartSpacing = true } = opts;
  if (!text) return;

  const value = el.value;
  const start = el.selectionStart ?? value.length;
  const end = el.selectionEnd ?? start;
  const before = value.slice(0, start);
  const after = value.slice(end);

  let insert = text;
  if (
    smartSpacing &&
    before.length > 0 &&
    !/\s$/.test(before) &&
    !/^[\s.,;:?!)]/.test(insert)
  ) {
    insert = ` ${insert}`;
  }

  setNativeValue(el, before + insert + after);
  const caret = start + insert.length;
  try {
    el.setSelectionRange(caret, caret);
  } catch {
    /* setSelectionRange throws for some input types — caret position is best-effort */
  }
  el.dispatchEvent(new Event('input', { bubbles: true }));
}
