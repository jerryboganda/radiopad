import { readHookPayload, writeHookResult } from './lib.mjs';

await readHookPayload();

writeHookResult({
  continue: true,
  systemMessage:
    'Open Design subagent hint: summarize the returned findings into decisions, changed paths, and risks. Avoid pasting large logs unless the user asked for raw output.',
});