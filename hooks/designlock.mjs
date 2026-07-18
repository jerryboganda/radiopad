import { filePathsFromPayload, readHookPayload, writeHookResult } from './lib.mjs';

// PreToolUse guard for the RC design lock (CLAUDE.md hard rules 1 & 7, docs/02-design/design.md).
// Advisory only: it asks for confirmation when a frontend .tsx/.css edit introduces a hardcoded
// colour, a forbidden UI library, or a legacy Hallmark alias. It never blocks outright and never
// edits files — a human approves genuinely-intended exceptions (e.g. the .op-bash terminal block).

function allow() {
  writeHookResult({
    continue: true,
    hookSpecificOutput: { hookEventName: 'PreToolUse', permissionDecision: 'allow' },
  });
  process.exit(0);
}

const payload = await readHookPayload();

const target = filePathsFromPayload(payload)
  .map((p) => p.replace(/\\/g, '/'))
  .find((p) => /\.(tsx|css)$/i.test(p));

if (!target) allow();

// tokens.css is the one sanctioned home for raw colours.
if (/(^|\/)frontend\/app\/tokens\.css$/i.test(target)) allow();

// Collect the proposed new text from the tool input (Write.content, Edit.new_string, MultiEdit.edits[]).
const ti = payload?.tool_input || payload?.toolInput || payload?.input || payload?.arguments || {};
let text = '';
if (typeof ti.content === 'string') text += `\n${ti.content}`;
if (typeof ti.new_string === 'string') text += `\n${ti.new_string}`;
if (Array.isArray(ti.edits)) {
  for (const e of ti.edits) if (typeof e?.new_string === 'string') text += `\n${e.new_string}`;
}
if (!text.trim()) allow();

const findings = [];

const hex = text.match(/#[0-9a-fA-F]{3,8}\b/g);
if (hex) findings.push(`hardcoded hex colour(s): ${[...new Set(hex)].slice(0, 5).join(', ')}`);
if (/\brgba?\(/i.test(text)) findings.push('hardcoded rgb()/rgba() colour');
if (/\bhsla?\(/i.test(text)) findings.push('hardcoded hsl()/hsla() colour');

const badImport = text.match(/from\s+['"](@mui\/[^'"]+|antd|@ant-design\/[^'"]+|@chakra-ui\/[^'"]+|bootstrap|react-bootstrap)['"]/i);
if (badImport) findings.push(`forbidden UI library import: ${badImport[1]}`);

const legacyVar = text.match(/(?:var\(\s*)?--(paper|saffron|marine)\b/i);
const legacyClass = text.match(/\b(?:bg|text|border|fill|stroke|ring)-(paper|saffron|marine)\b/i);
if (legacyVar || legacyClass) {
  findings.push(`legacy Hallmark alias (${(legacyVar || legacyClass)[1]}) — use the RC token names`);
}

if (findings.length === 0) allow();

writeHookResult({
  continue: true,
  systemMessage: `RadioPad design lock flagged ${target}: ${findings.join('; ')}.`,
  hookSpecificOutput: {
    hookEventName: 'PreToolUse',
    permissionDecision: 'ask',
    permissionDecisionReason:
      `Design-lock violation in ${target}: ${findings.join('; ')}. ` +
      'Use tokens.css variables / documented .rp-* classes and lucide-react icons instead. ' +
      'Confirm only if this is a documented exception (e.g. the .op-bash terminal block or present-mode surface), and verify BOTH light and dark themes.',
  },
});
