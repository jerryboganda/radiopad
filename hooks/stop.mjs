import { readHookPayload, writeHookResult } from './lib.mjs';

await readHookPayload();

writeHookResult({
  continue: true,
  systemMessage:
    'Open Design completion hint: final responses should name changed files, checks run, and any honest limits. Do not omit failing checks or unverified runtime assumptions.',
});