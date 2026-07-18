// F1 (dictation brief) — spoken-measurement formatting for the LIVE dictation path.
//
// Turns spoken number words into digits and abbreviates measurement units, so a radiologist
// who says "three point five centimetres" sees "3.5 cm" the moment it lands in the editor —
// not just later when the draft is formatted server-side.
//
// This is a faithful TypeScript port of the backend's deterministic §5.2 pass-through
// (`DeterministicPassThrough.NormalizeSpokenNumbers`). Keeping the two in lock-step matters:
// the draft endpoint re-runs the backend normaliser on whatever text we send it, so producing
// the SAME digit form here makes that second pass a no-op (idempotent) rather than a diff the
// §5.3 validator could trip over. Pure + deterministic; every rule is unit-tested.
//
// Only number *words* are converted — a digit that is already typed ("2 mm") never starts an
// expression, so existing digit text passes through untouched.

const UNITS: Record<string, number> = {
  zero: 0, one: 1, two: 2, three: 3, four: 4, five: 5, six: 6, seven: 7, eight: 8, nine: 9,
  ten: 10, eleven: 11, twelve: 12, thirteen: 13, fourteen: 14, fifteen: 15, sixteen: 16,
  seventeen: 17, eighteen: 18, nineteen: 19, twenty: 20, thirty: 30, forty: 40, fifty: 50,
  sixty: 60, seventy: 70, eighty: 80, ninety: 90,
};

const SCALES: Record<string, number> = { hundred: 100, thousand: 1000, million: 1_000_000 };

const UNIT_WORDS: Record<string, string> = {
  mm: 'mm', millimeter: 'mm', millimeters: 'mm', millimetre: 'mm', millimetres: 'mm',
  cm: 'cm', centimeter: 'cm', centimeters: 'cm', centimetre: 'cm', centimetres: 'cm',
};

const TRIM_PUNCT = new Set(['.', ',', ';', ':', '(', ')', '"', "'", '!', '?']);

interface Tok {
  raw: string;
  core: string;
  sep: string;
}

/** Lowercased token with surrounding punctuation stripped from both ends (mirrors backend `Core`). */
function core(raw: string): string {
  let start = 0;
  let end = raw.length;
  while (start < end && TRIM_PUNCT.has(raw[start])) start++;
  while (end > start && TRIM_PUNCT.has(raw[end - 1])) end--;
  return raw.slice(start, end).toLowerCase();
}

function tokenize(text: string): Tok[] {
  const out: Tok[] = [];
  const re = /(\S+)(\s*)/g;
  let m: RegExpExecArray | null;
  while ((m = re.exec(text)) !== null) {
    out.push({ raw: m[1], core: core(m[1]), sep: m[2] });
  }
  return out;
}

const isNumberStart = (c: string): boolean => c in UNITS || c in SCALES;
const isIntegerWord = (c: string): boolean => c in UNITS || c in SCALES;

/** Parse a single cardinal number (with optional "point" decimals) into a digit string. */
function consumeOneNumber(tokens: Tok[], start: number): [string, number] {
  let acc = 0;
  let current = 0;
  let saw = false;
  let i = start;

  while (i < tokens.length) {
    const c = tokens[i].core;
    if (c in UNITS) {
      current += UNITS[c];
      saw = true;
      i++;
    } else if (c === 'hundred') {
      current = (current === 0 ? 1 : current) * 100;
      saw = true;
      i++;
    } else if (c in SCALES && SCALES[c] >= 1000) {
      acc += (current === 0 ? 1 : current) * SCALES[c];
      current = 0;
      saw = true;
      i++;
    } else if (c === 'and' && saw && i + 1 < tokens.length && isIntegerWord(tokens[i + 1].core)) {
      i++; // "one hundred AND twenty"
    } else {
      break;
    }
  }

  if (!saw) return ['', 0];

  let intVal = String(acc + current);

  // Optional decimal: "point" followed by single-digit words read individually.
  if (i < tokens.length && tokens[i].core === 'point') {
    let j = i + 1;
    let dec = '';
    while (j < tokens.length && tokens[j].core in UNITS && UNITS[tokens[j].core] < 10) {
      dec += String(UNITS[tokens[j].core]);
      j++;
    }
    if (dec.length > 0) {
      intVal = `${intVal}.${dec}`;
      i = j;
    }
  }

  return [intVal, i - start];
}

/** Parse one or more number groups joined by "by"/"x" plus an optional trailing unit. */
function consumeNumberExpression(tokens: Tok[], start: number): [string, number] {
  let i = start;
  const groups: string[] = [];

  while (i < tokens.length) {
    const [num, used] = consumeOneNumber(tokens, i);
    if (used === 0) break;
    groups.push(num);
    i += used;

    if (
      i < tokens.length &&
      (tokens[i].core === 'by' || tokens[i].core === 'x') &&
      i + 1 < tokens.length &&
      isNumberStart(tokens[i + 1].core)
    ) {
      i++; // consume the "by"/"x" separator and keep collecting axes
      continue;
    }
    break;
  }

  if (groups.length === 0) return ['', 0];

  let joined = groups.join(' x ');
  if (i < tokens.length && tokens[i].core in UNIT_WORDS) {
    joined += ` ${UNIT_WORDS[tokens[i].core]}`;
    i++;
  }

  return [joined, i - start];
}

/**
 * Replace spoken number expressions in `text` with their digit form
 * ("two by three centimetres" → "2 x 3 cm"). Non-number tokens pass through verbatim.
 */
export function normalizeSpokenNumbers(text: string): string {
  if (!text) return text ?? '';

  const tokens = tokenize(text);
  let out = '';
  let i = 0;

  while (i < tokens.length) {
    if (isNumberStart(tokens[i].core)) {
      const [digits, used] = consumeNumberExpression(tokens, i);
      if (used > 0) {
        out += digits;
        out += tokens[i + used - 1].sep;
        i += used;
        continue;
      }
    }
    out += tokens[i].raw;
    out += tokens[i].sep;
    i++;
  }

  return out;
}
