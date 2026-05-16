export async function readHookPayload() {
  let input = '';
  for await (const chunk of process.stdin) {
    input += chunk;
  }

  if (!input.trim()) return {};

  try {
    return JSON.parse(input);
  } catch {
    return { rawInput: input };
  }
}

export function writeHookResult(result) {
  process.stdout.write(`${JSON.stringify(result, null, 2)}\n`);
}

export function toolNameFromPayload(payload) {
  return firstString([
    payload?.tool_name,
    payload?.toolName,
    payload?.name,
    payload?.tool?.name,
    payload?.tool?.identifier,
    payload?.hookSpecificInput?.toolName,
  ]);
}

export function commandFromPayload(payload) {
  const candidates = [
    payload?.command,
    payload?.cmd,
    payload?.script,
    payload?.input?.command,
    payload?.input?.cmd,
    payload?.tool_input?.command,
    payload?.toolInput?.command,
    payload?.arguments?.command,
    payload?.params?.command,
    payload?.hookSpecificInput?.toolInput?.command,
  ];
  return firstString(candidates) || findNestedString(payload, new Set(['command', 'cmd', 'script']));
}

export function filePathsFromPayload(payload) {
  const paths = new Set();
  collectPathLikeStrings(payload, paths, 0);
  return [...paths].filter((value) => /[\\/]|\.[A-Za-z0-9]{1,8}$/.test(value));
}

function firstString(values) {
  for (const value of values) {
    if (typeof value === 'string' && value.trim()) return value.trim();
  }
  return '';
}

function findNestedString(value, keys, depth = 0) {
  if (!value || depth > 5) return '';
  if (Array.isArray(value)) {
    for (const item of value) {
      const found = findNestedString(item, keys, depth + 1);
      if (found) return found;
    }
    return '';
  }
  if (typeof value !== 'object') return '';

  for (const [entryKey, entryValue] of Object.entries(value)) {
    if (keys.has(entryKey) && typeof entryValue === 'string' && entryValue.trim()) {
      return entryValue.trim();
    }
    const found = findNestedString(entryValue, keys, depth + 1);
    if (found) return found;
  }
  return '';
}

function collectPathLikeStrings(value, paths, depth) {
  if (!value || depth > 6) return;
  if (typeof value === 'string') {
    if (value.length < 260) paths.add(value.trim());
    return;
  }
  if (Array.isArray(value)) {
    for (const item of value) collectPathLikeStrings(item, paths, depth + 1);
    return;
  }
  if (typeof value !== 'object') return;

  for (const [entryKey, entryValue] of Object.entries(value)) {
    const keyLooksPathy = /path|file|filename|name/i.test(entryKey);
    if (keyLooksPathy && typeof entryValue === 'string' && entryValue.trim()) {
      paths.add(entryValue.trim());
    }
    collectPathLikeStrings(entryValue, paths, depth + 1);
  }
}