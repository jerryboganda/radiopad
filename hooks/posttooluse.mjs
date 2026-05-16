import { filePathsFromPayload, readHookPayload, writeHookResult } from './lib.mjs';

const payload = await readHookPayload();
const touchedPaths = filePathsFromPayload(payload);
const validationSensitive = touchedPaths.some((filePath) =>
  /^(src|app|daemon|tests|scripts)\//.test(filePath.replace(/\\/g, '/')) ||
  /(^|\/)(package\.json|pnpm-lock\.yaml|tsconfig\.json|next\.config\.ts|vitest\.config\.ts)$/.test(filePath.replace(/\\/g, '/')),
);

if (validationSensitive) {
  writeHookResult({
    continue: true,
    systemMessage:
      'Open Design validation hint: source, test, script, or config files changed. Run `pnpm typecheck` and the narrowest relevant `pnpm test` before finalizing, or state why they could not run.',
  });
} else {
  writeHookResult({ continue: true });
}