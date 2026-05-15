/**
 * PRD RB-002 — Rulebook visual editor YAML ↔ structured-data conversion.
 *
 * Uses simple string templating for YAML generation so we avoid adding a
 * YAML library dependency. The YAML → state parser is line-oriented, matching
 * the stable indentation-driven shape validated server-side by
 * `RulebookSpec.FromYaml`.
 */

/* ------------------------------------------------------------------ */
/*  Types                                                             */
/* ------------------------------------------------------------------ */

export type RulebookRule = {
  id: string;
  severity: 'blocker' | 'warning' | 'info';
  description: string;
};

export type RulebookPromptBlock = {
  key: string;
  text: string;
};

export type RulebookStyle = {
  tone: string;
  impression_max_bullets: number;
  avoid_terms: string[];
  approved_followups: string[];
};

export type RulebookSection = {
  name: string;
  required: boolean;
};

export type RulebookEditorState = {
  rulebook_id: string;
  name: string;
  version: string;
  owner: string;
  status: string;
  applies_to: {
    modalities: string[];
    body_parts: string[];
    report_types: string[];
  };
  style: RulebookStyle;
  required_sections: RulebookSection[];
  rules: RulebookRule[];
  prompt_blocks: RulebookPromptBlock[];
};

/* ------------------------------------------------------------------ */
/*  Defaults                                                          */
/* ------------------------------------------------------------------ */

export function emptyEditorState(): RulebookEditorState {
  return {
    rulebook_id: '',
    name: '',
    version: '1.0.0',
    owner: '',
    status: 'draft',
    applies_to: { modalities: [], body_parts: [], report_types: [] },
    style: {
      tone: 'concise_clinical',
      impression_max_bullets: 5,
      avoid_terms: [],
      approved_followups: [],
    },
    required_sections: [
      { name: 'Indication', required: true },
      { name: 'Technique', required: true },
      { name: 'Comparison', required: false },
      { name: 'Findings', required: true },
      { name: 'Impression', required: true },
      { name: 'Recommendations', required: false },
    ],
    rules: [],
    prompt_blocks: [],
  };
}

/* ------------------------------------------------------------------ */
/*  State → YAML                                                      */
/* ------------------------------------------------------------------ */

function yamlStr(v: string): string {
  if (!v) return '""';
  if (/[:#\[\]{},&*!|>'"@`]/.test(v) || v.trim() !== v) {
    return `"${v.replace(/\\/g, '\\\\').replace(/"/g, '\\"')}"`;
  }
  return v;
}

function yamlList(items: string[], indent: number): string {
  const pad = ' '.repeat(indent);
  if (items.length === 0) return ' []';
  return '\n' + items.map((i) => `${pad}- ${yamlStr(i)}`).join('\n');
}

export function rulebookToYaml(data: RulebookEditorState): string {
  const lines: string[] = [];

  lines.push(`rulebook_id: ${yamlStr(data.rulebook_id)}`);
  lines.push(`name: ${yamlStr(data.name)}`);
  lines.push(`version: ${yamlStr(data.version)}`);
  lines.push(`owner: ${yamlStr(data.owner)}`);
  lines.push(`status: ${yamlStr(data.status)}`);

  // applies_to
  lines.push('applies_to:');
  lines.push(`  modalities:${yamlList(data.applies_to.modalities, 4)}`);
  lines.push(`  body_parts:${yamlList(data.applies_to.body_parts, 4)}`);
  lines.push(`  report_types:${yamlList(data.applies_to.report_types, 4)}`);

  // style
  lines.push('style:');
  lines.push(`  tone: ${yamlStr(data.style.tone)}`);
  lines.push(`  impression_max_bullets: ${data.style.impression_max_bullets}`);
  lines.push(`  avoid_terms:${yamlList(data.style.avoid_terms, 4)}`);
  lines.push(`  approved_followups:${yamlList(data.style.approved_followups, 4)}`);

  // required_sections
  const reqNames = data.required_sections
    .filter((s) => s.required)
    .map((s) => s.name);
  lines.push(`required_sections:${yamlList(reqNames, 2)}`);

  // rules
  if (data.rules.length > 0) {
    lines.push('rules:');
    for (const r of data.rules) {
      lines.push(`  - id: ${yamlStr(r.id)}`);
      lines.push(`    severity: ${r.severity}`);
      if (r.description) {
        lines.push(`    description: ${yamlStr(r.description)}`);
      }
    }
  }

  // prompt_blocks
  if (data.prompt_blocks.length > 0) {
    lines.push('prompt_blocks:');
    for (const pb of data.prompt_blocks) {
      lines.push(`  ${pb.key}: |`);
      for (const tl of pb.text.split('\n')) {
        lines.push(`    ${tl}`);
      }
    }
  }

  return lines.join('\n') + '\n';
}

/* ------------------------------------------------------------------ */
/*  YAML → State                                                      */
/* ------------------------------------------------------------------ */

function stripStr(s: string): string {
  let v = s.trim();
  if ((v.startsWith('"') && v.endsWith('"')) || (v.startsWith("'") && v.endsWith("'"))) {
    v = v.slice(1, -1);
  }
  return v;
}

function parseInlineList(line: string): string[] {
  const m = line.match(/\[(.*)\]/);
  if (!m) return [];
  return m[1]
    .split(',')
    .map((t) => stripStr(t.trim()))
    .filter(Boolean);
}

type Section =
  | 'none'
  | 'applies_modalities'
  | 'applies_bodyparts'
  | 'applies_reporttypes'
  | 'avoid'
  | 'followups'
  | 'required_sections'
  | 'rules'
  | 'prompts';

export function yamlToRulebookEditor(yaml: string): RulebookEditorState {
  const state = emptyEditorState();
  state.required_sections = [];

  const lines = yaml.split(/\r?\n/);
  let section: Section = 'none';
  let currentRule: RulebookRule | null = null;
  let currentPromptKey = '';
  let currentPromptLines: string[] = [];

  function flushRule() {
    if (currentRule) {
      state.rules.push(currentRule);
      currentRule = null;
    }
  }
  function flushPrompt() {
    if (currentPromptKey) {
      state.prompt_blocks.push({ key: currentPromptKey, text: currentPromptLines.join('\n') });
      currentPromptKey = '';
      currentPromptLines = [];
    }
  }

  for (const raw of lines) {
    const line = raw.trimEnd();
    if (!line.trim()) continue;

    // Top-level scalar fields
    const scalarMatch = line.match(/^([a-z_]+)\s*:\s*(.+)$/);
    if (scalarMatch && !/^\s/.test(line)) {
      const [, key, val] = scalarMatch;
      const sv = stripStr(val);
      switch (key) {
        case 'rulebook_id': state.rulebook_id = sv; section = 'none'; continue;
        case 'name': state.name = sv; section = 'none'; continue;
        case 'version': state.version = sv; section = 'none'; continue;
        case 'owner': state.owner = sv; section = 'none'; continue;
        case 'status': state.status = sv; section = 'none'; continue;
      }
    }

    // Section headers (top-level)
    if (/^applies_to\s*:\s*$/.test(line)) { flushRule(); flushPrompt(); section = 'none'; continue; }
    if (/^style\s*:\s*$/.test(line)) { flushRule(); flushPrompt(); section = 'none'; continue; }

    // Nested under applies_to
    if (/^\s+modalities\s*:\s*\[/.test(line)) {
      state.applies_to.modalities = parseInlineList(line);
      continue;
    }
    if (/^\s+modalities\s*:\s*$/.test(line)) { section = 'applies_modalities'; continue; }
    if (/^\s+body_parts\s*:\s*\[/.test(line)) {
      state.applies_to.body_parts = parseInlineList(line);
      continue;
    }
    if (/^\s+body_parts\s*:\s*$/.test(line)) { section = 'applies_bodyparts'; continue; }
    if (/^\s+report_types\s*:\s*\[/.test(line)) {
      state.applies_to.report_types = parseInlineList(line);
      continue;
    }
    if (/^\s+report_types\s*:\s*$/.test(line)) { section = 'applies_reporttypes'; continue; }

    // Nested under style
    if (/^\s+tone\s*:\s*(.+)$/.test(line)) {
      const m = line.match(/^\s+tone\s*:\s*(.+)$/);
      if (m) state.style.tone = stripStr(m[1]);
      continue;
    }
    if (/^\s+impression_max_bullets\s*:\s*(\d+)/.test(line)) {
      const m = line.match(/^\s+impression_max_bullets\s*:\s*(\d+)/);
      if (m) state.style.impression_max_bullets = parseInt(m[1], 10);
      continue;
    }
    if (/^\s+avoid_terms\s*:\s*\[/.test(line)) {
      state.style.avoid_terms = parseInlineList(line);
      continue;
    }
    if (/^\s+avoid_terms\s*:\s*$/.test(line)) { section = 'avoid'; continue; }
    if (/^\s+approved_followups\s*:\s*$/.test(line)) { section = 'followups'; continue; }

    // required_sections
    if (/^required_sections\s*:\s*\[/.test(line)) {
      const items = parseInlineList(line);
      state.required_sections = items.map((n) => ({ name: n, required: true }));
      section = 'none';
      continue;
    }
    if (/^required_sections\s*:\s*$/.test(line)) { flushRule(); flushPrompt(); section = 'required_sections'; continue; }

    // rules
    if (/^rules\s*:\s*$/.test(line)) { flushPrompt(); section = 'rules'; continue; }

    // prompt_blocks
    if (/^prompt_blocks\s*:\s*$/.test(line)) { flushRule(); section = 'prompts'; continue; }

    // List items based on current section
    if (/^\s*-\s/.test(line)) {
      const item = stripStr(line.replace(/^\s*-\s+/, ''));
      switch (section) {
        case 'applies_modalities': state.applies_to.modalities.push(item); continue;
        case 'applies_bodyparts': state.applies_to.body_parts.push(item); continue;
        case 'applies_reporttypes': state.applies_to.report_types.push(item); continue;
        case 'avoid': state.style.avoid_terms.push(item); continue;
        case 'followups': state.style.approved_followups.push(item); continue;
        case 'required_sections': state.required_sections.push({ name: item, required: true }); continue;
        case 'rules': {
          const idMatch = line.match(/^\s*-\s*id\s*:\s*(.+)$/);
          if (idMatch) {
            flushRule();
            currentRule = { id: stripStr(idMatch[1]), severity: 'warning', description: '' };
          }
          continue;
        }
      }
    }

    // Rule sub-fields
    if (section === 'rules' && currentRule) {
      const sevMatch = line.match(/^\s+severity\s*:\s*(.+)$/);
      const descMatch = line.match(/^\s+description\s*:\s*(.+)$/);
      if (sevMatch) {
        const sv = stripStr(sevMatch[1]).toLowerCase() as RulebookRule['severity'];
        if (sv === 'blocker' || sv === 'warning' || sv === 'info') currentRule.severity = sv;
      } else if (descMatch) {
        currentRule.description = stripStr(descMatch[1]);
      }
      continue;
    }

    // Prompt block parsing
    if (section === 'prompts') {
      const keyMatch = line.match(/^\s{2}([a-z_][a-z0-9_]*)\s*:\s*\|?\s*$/i);
      if (keyMatch) {
        flushPrompt();
        currentPromptKey = keyMatch[1];
        continue;
      }
      // Content line for current prompt block (indented ≥4 spaces)
      if (currentPromptKey && /^\s{4}/.test(line)) {
        currentPromptLines.push(line.slice(4));
        continue;
      }
    }
  }

  flushRule();
  flushPrompt();

  return state;
}
