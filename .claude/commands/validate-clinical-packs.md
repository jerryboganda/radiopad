---
description: Validate all rulebooks (with golden cases) and check every JSON template is well-formed.
allowed-tools: Bash(dotnet run --project cli/RadioPad.Cli:*), Bash(node:*)
---

Run the full local content gate for the clinical packs (rulebooks + templates).

1. **Rulebooks** — for each `rulebooks/*.yaml`:
   `dotnet run --project cli/RadioPad.Cli -- rulebook validate <file>`
   Where `rulebooks/_tests/<id>/` exists, also run the golden cases:
   `dotnet run --project cli/RadioPad.Cli -- rulebook test rulebooks/<id>.yaml --cases rulebooks/_tests/<id>`
2. **Templates** — verify every `templates/**/*.json` parses (well-formedness). The CLI `templates import` only does `JsonDocument.Parse`, so a structurally-broken template otherwise ships silently. A quick node check is fine, e.g.:
   `node -e "const fs=require('fs'),g=require('glob');for(const f of g.sync('templates/**/*.json')){try{JSON.parse(fs.readFileSync(f,'utf8'))}catch(e){console.error('BAD',f,e.message);process.exitCode=1}}"`
   (or a plain recursive scan if `glob` is unavailable).

Print a pass/fail summary grouped by **rulebooks** vs **templates**, and stop-detail on the first failure.
