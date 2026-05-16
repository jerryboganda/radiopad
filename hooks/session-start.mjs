import { readHookPayload, writeHookResult } from './lib.mjs';

await readHookPayload();

writeHookResult({
  continue: true,
  systemMessage:
    'Open Design context: Next.js app lives in `app/` and `src/`; local daemon lives in `daemon/`; committed skills live in `skills/`; design systems live in `design-systems/`; runtime data under `.od/` is ignored. Prefer existing helpers, protect user work, and validate with `pnpm typecheck` plus relevant `pnpm test` when code changes.',
});