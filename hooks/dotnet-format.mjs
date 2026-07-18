import { execSync } from 'node:child_process';
import { filePathsFromPayload, readHookPayload, writeHookResult } from './lib.mjs';

// PostToolUse formatter for backend/CLI C# edits. OPT-IN: does nothing unless
// RADIOPAD_DOTNET_FORMAT_HOOK=1 is set, so it never slows the default edit loop
// (the operator runs heavy work in CI). When enabled, it applies whitespace/style
// fixes to just the edited file per .editorconfig.

function done(extra = {}) {
  writeHookResult({ continue: true, ...extra });
  process.exit(0);
}

const payload = await readHookPayload();

if (process.env.RADIOPAD_DOTNET_FORMAT_HOOK !== '1') done();

const target = filePathsFromPayload(payload)
  .map((p) => p.replace(/\\/g, '/'))
  .find((p) => /\.cs$/i.test(p) && /^(backend|cli)\//.test(p));

if (!target) done();

const project = target.startsWith('backend/')
  ? 'backend/RadioPad.Api/RadioPad.Api.sln'
  : 'cli/RadioPad.Cli/RadioPad.Cli.csproj';

try {
  execSync(`dotnet format "${project}" whitespace --include "${target}" --no-restore`, {
    cwd: process.cwd(),
    stdio: 'ignore',
  });
  done();
} catch {
  done({
    systemMessage: `dotnet format could not tidy ${target} automatically — check whitespace/style (.editorconfig) before committing.`,
  });
}
