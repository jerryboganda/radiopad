# RadioPad — AI-Assisted Dev Environment Audit & Hardening Backlog

**Status:** Current · **Owner:** Engineering · **Date:** 2026-07-18 · **Method:** 9-agent evidence-based static audit (no builds/tests run — CI-only rule honoured)

This report is the authoritative record of the 2026-07-18 audit that prepared RadioPad for optimized, secure AI-assisted development. Part A lists changes already applied to the working tree. Part B is the prioritized hardening backlog — ready-to-apply specs for items that were intentionally **not** auto-applied because they need compilation, a migration, or a human decision.

---

## Executive summary

RadioPad is a healthcare/PHI radiology-reporting platform on a strong, locked stack (Next.js 16 + React 18 · ASP.NET Core 8 + EF Core 8 · Tauri 2 signed auto-update · Capacitor 6 companion · .NET 8 CLI · pnpm monorepo). Engineering foundations are solid: clean backend layering, a tamper-tested PHI-block + append-only SHA-256 audit chain, KMS envelope encryption, WebAuthn UV, least-privilege Tauri capabilities, 100+ backend / ~40 frontend tests, OIDC desktop signing.

The dominant problem was **drift and staleness, not missing tooling** — the repo was scaffolded from an ancestor product ("Open Design"), and its branding, agent-kit, skills, and design-system language were still present and actively misdirecting AI agents. This audit reconciled the agent-facing contract to the real code (the RC design system) and removed the dead Open Design layer.

---

## Part A — Changes applied in this pass (working tree; not committed)

### A1. Design-system reconciliation (resolves the #1 hazard)
Ratified **CLAUDE.md / RC** (dual-theme, dark mandatory, `frontend/app/tokens.css`, Tailwind 3) as the single source of truth. Rewrote the contradictory sources to thin pointers:
- `CLAUDE.md` — removed duplicated H1; added a precedence banner (this file is authoritative).
- `AGENTS.md` §0 — replaced the "Hallmark / dark-forbidden / hallmark.css (non-existent)" block with an RC pointer + precedence line.
- `GEMINI.md` — replaced the "Open Design warm-paper / Tailwind-forbidden / dark-forbidden" block with an RC pointer.
- `docs/INDEX.md` — locked-UI banner now names RC; removed the `INDEX.legacy.md` pointer.
- `frontend/app/radiopad.css` — header now cites RC `tokens.css` (was "Open Design tokens in globals.css").
- `mobile/README.md` — Design-lock section now cites RC `tokens.css` + splash `#f5f8fb` (was Open Design `globals.css` + `#faf9f7`).
- `CLAUDE.md` / `AGENTS.md` repo maps — replaced the deleted `src/`/`daemon/`/`*.legacy.*` entries with `subagents/` + `mcp-connectors/`; AGENTS §2 map corrected "Hallmark" → RC.

### A2. Legacy "Open Design" removal (289 files staged via `git rm`)
- Dead code trees: `src/` (58), `daemon/` (19), `app/` (3), `design-systems/` (73), root `skills/` (80), root `tests/` (4), `plugins/` (3).
- `*.legacy.*` (11): `CLAUDE.legacy.md`, `README*.legacy.md`, `CONTRIBUTING*.legacy.md`, `QUICKSTART.legacy.md`, `next.config.legacy.ts`, `tsconfig.legacy.json`, `vitest.config.legacy.ts`, `package.legacy.json`, `docs/INDEX.legacy.md`; plus orphan `next-env.d.ts` (root).
- Generated artifacts: `.gap_areas.json`, `.gap_clean.json`, `.gap_inventory.md`, `.gap_synthesis.md` (~1.16 MB).
- Iteration cruft: `iter31-C.md`, `iter32-*.cmd/.ps1`, `scripts/iter31-c-verify`, `scripts/iter32_rulebook_update.ps1`.
- Live legacy coupling cut first: `scripts/runtime-adapter.e2e.live.test.mjs`, `scripts/launch-open-design.ps1`, `scripts/sync-design-systems.mjs`.
- Open Design agent-kit: `.github/hooks/open-design-agent-kit.json`, the four `.github/agents/open-design-*.agent.md` Copilot dupes, and the Open Design lifecycle hook scripts (`hooks/session-start*`, `posttooluse*`, `stop*`, `subagent-stop*`, `*.sh`).
- `skills-lock.json` (orphaned by `skills/`), and the stray `nul` file in the parent dir.
- Verified beforehand: no active code references any deleted path (all `src/` hits in live code are `backend/RadioPad.Api/src/…`, `marketing/src/…`, or `android/…/src/…`).

### A3. AI-tooling rebrand & guard salvage
- Rewrote the 4 `subagents/*.md` (explorer, code-reviewer, test-runner, feature-dev) to RadioPad's real map + safety boundaries + the CI-only rule (were Open Design / daemon / BYOK).
- Salvaged the destructive-command **PreToolUse guard** (`hooks/pretooluse.{mjs,ps1}` + `lib.{mjs,ps1}`): rebranded messaging, rewrote `hooks/README.md` (guard-only), and wired it via a local `.claude/settings.json` (gitignored per repo policy — local-only until tracked deliberately).
- `.github/instructions/security.instructions.md` — replaced the false "v0.1 header-based dev tenant" claim with the real auth stack (WebAuthn UV / SAML / OIDC / SCIM / bearer+lockout; `RADIOPAD_DEV_HEADERS` dev-only) and removed the unbacked "weekly SCA review" line.

### A4. Hygiene & safe config fixes
- `.gitignore` — fixed the U+FFFD mojibake; added `.env`/`.env.*` (+ `!.env.example`), `*.key/*.pem/*.pfx/*.p12/*.sec/*.secret`, `.secrets.env`, `.codegraph/`, `.gap_*`; dropped the dead daemon `.od`/`.ocd` and iter test-log ignores. (Gitignore does not untrack existing files — the tracked tree is unaffected.)
- `mobile/capacitor.config.ts` — `cleartext` now defaults **off** (`process.env.RADIOPAD_MOBILE_CLEARTEXT === '1'`) so production APKs can't downgrade PHI transport; dev opts in locally.
- `mobile/package.json` — moved `@aparajita/capacitor-biometric-auth` + `@capacitor/push-notifications` from `devDependencies` → `dependencies` (they are runtime security features that `--prod` installs would otherwise drop).
- `.github/workflows/desktop-release.yml` — build step `build` → `build:desktop` (Tauri consumes `out-desktop/`, plain `build` emits `out/`).
- `desktop/src-tauri/Cargo.toml` — corrected the inverted "no committed Cargo.lock" comment (a lock IS committed).
- `desktop/src-tauri/entitlements.plist` — DOCTYPE `apple.dev` → `apple.com`.

> **Validation caveat:** per the project's CI-only rule these edits were not built/tested locally. `mobile/package.json`, `capacitor.config.ts`, and the workflow YAML must be validated by a green Actions run before merge.

---

## Part B — Hardening backlog (ready-to-apply specs; NOT yet applied)

These were withheld from the auto-apply pass because they need compilation, a data migration, or an operational decision. Each is safe to schedule as its own reviewed PR with CI validation.

### B1. Backend / PHI (highest value)
1. **EF Core global query filters for tenant isolation.** No `HasQueryFilter` exists in `RadioPadDbContext.OnModelCreating`; every tenant-scoped query relies on a manual `.Where(x => x.TenantId == tenant.Id)`. Add an `ITenantScoped` marker + a scoped/AsyncLocal tenant accessor set in middleware + `HasQueryFilter` for each scoped entity; keep explicit predicates as belt-and-suspenders. Add an architecture test asserting every `ITenantScoped` entity has a filter, plus a cross-tenant negative test per controller family. *(Migration-free; needs `dotnet build` + new tests in CI. Audit any admin/cross-tenant path for needed `IgnoreQueryFilters`.)*
2. **PHI detector.** `ReportingService.ContainsPhiText` is a self-admitted non-production regex (~5 patterns vs HIPAA's 18 identifiers). Treat free text as **PHI-by-default (fail-closed)** for PHI-locked tenants; integrate a maintained clinical PHI/PII NER behind the existing `IAiGateway` boundary; add a CI regression corpus that MUST classify as PHI. Do not weaken the (correct, tamper-tested) gate.
3. **Audit-chain hardening (needs migration window).** `Repositories.cs` hash payload omits `UserId` + `CreatedAt` and is an unkeyed SHA-256. Include every immutable field, switch to a KMS/HSM-keyed HMAC (or externally-anchored Merkle roots), and enforce append-only at the DB (revoke UPDATE/DELETE or use an `INSTEAD OF` trigger). Requires a rechain/backfill plan — **operator sign-off required.** Also fix the `RadioPadDbContext.cs:152-157` comment overstating model-level append-only protection.
4. **Enforcement in CI.** Add a `dotnet build -warnaserror` step (nullable is enabled but `TreatWarningsAsErrors=false`, so violations never fail a PR), excluding generated migrations; add `coverlet.collector` + `--collect:"XPlat Code Coverage"` with a floor on `AiGateway`/`EfAuditLog`/`TenantedController`.

### B2. Desktop / mobile
1. **Windows plugin sandbox** (`desktop/src-tauri/src/sandbox.rs:470-491`) is a no-op passthrough while `PLUGIN_TRUST.md:170-172` advertises AppContainer isolation. Either implement real AppContainer/WDAC confinement (ship the launcher + populate the `.cip`) or mark the desktop sandbox unused and correct the docs — do not claim an unimplemented isolation guarantee.
2. **Updater/installer doc drift** (`UPDATE_SIGNING.md`, `INSTALLER_HARDENING.md`) contradicts the shipped config on 4 points (host, key storage, empty-pubkey, cert-thumbprint). Reconcile to the real GitHub-releases single-key updater + `radiopadstudio.com`.
3. **PACS manifest verification** (`pacs_plugins.rs` + `sandbox.rs:180-207`): the desktop SHA-256/signature base (raw bytes) diverges from the schema/back-end (canonical JSON with the `sha256` field excluded) → every plugin resolves `verified:false`; and enable is decoupled from verification. Unify the hashing/signature base; refuse to enable unverified plugins.
4. **Updater feed** is served from a personal GitHub account (`jerryboganda/radiopad`) — move to an org-owned repo with branch/tag protection.
5. **Mobile**: pin `@aparajita/capacitor-biometric-auth` to its Capacitor-6 line (currently `^7.0.0` = Cap 7); declare CAMERA/`NSCameraUsageDescription` for the barcode/QR-pairing dependency (or remove `@capacitor-mlkit/barcode-scanning`).

### B3. CLI
1. **`PhiGuard` is dead code** (`cli/RadioPad.Cli/Commands/PhiGuard.cs:47-64`): compares an int-serialized `compliance` against string enum names → `radiopad generate --provider <guid>` on PHI **always** over-blocks. Parse both int and string forms (or add `JsonStringEnumConverter` server-side); add unit tests.
2. Add `RadioPad.Cli` to `RadioPad.Api.sln` and give the CLI job an explicit `dotnet test` step (coverage currently rides on one transitive `ProjectReference`).
3. Add a tag-triggered `dotnet pack`/publish job (the tool declares `PackAsTool` but nothing packages it), and pick a version source of truth.
4. Add xUnit tests for `PhiGuard`, `McpServer` scope-gating, and the audit-chain verifier.

### B4. CI / deploy hardening (per your directive, the SCA/SAST/gitleaks/Dependabot/ESLint bundle is **out of scope**)
1. SHA-pin all third-party Actions (`dtolnay/rust-toolchain@stable`, `softprops/action-gh-release@v2`, `aws-actions/configure-aws-credentials@v4`, `pnpm/action-setup`, …) — they run in workflows holding `contents:write` + AWS OIDC + signing secrets. *(Requires looking up each action's release SHA — do at PR time.)*
2. Add top-level `permissions: { contents: read }` to the 5 workflows lacking it; switch the PR-gate installs (`ci.yml`, `mobile-bundle.yml`) to `--frozen-lockfile`.
3. Add a non-root `USER` to the API Dockerfiles and pin the Orthanc base image off `:latest`.
4. Reconcile the production edge config: the committed `deploy/Caddyfile.prod` / `deploy/vps/nginx.conf` are **not** the live front door (hand-maintained NPM + Cloudflare). Establish one committed, version-controlled vhost source deployed idempotently.

### B5. Docs & repo hygiene (decisions)
1. **Ops scripts that mutate production** (`scripts/fix-login.*`, `fix-cf-origin.sh`, `fix-npm-cache.py`, `add-user*.sh`, `send-magic*.sh`, …) bypass git+CI and already caused a login outage; the SQLite user-insert scripts also bypass the audit log + tenant scoping. **Recommend removal**, folding intent into CI-deployed source + the audited CLI path — held for your confirmation (operational, not "Open Design legacy").
2. PRD sprawl: designate `RadioPad_Enterprise_PRD_v3.0.md` as canonical; archive/remove the v2 copy, the Google-Docs PDF, and the `(1)` duplicate; update `PRD.md`'s pointer.
3. Docs numbering: `docs/` has duplicate `05-*` (data-ai vs clinical) and `06-*` (operations vs testing) folders; renumbering touches many intra-doc links — schedule as its own PR.
4. `.mcp.json` (optional): pin read-only dev MCP (Serena, CodeGraph, optionally context7). **Not created here** — the exact launch commands for this environment's servers are unknown, and a fabricated command would be a placeholder. Fill in with your actual server commands; never include the clinical `mcp-connectors/` or any DB/write MCP.

---

## Tooling / MCP / agent decisions (summary)

- **Retain:** xUnit, Vitest+Testing Library, .NET analyzers+nullable (make enforcing), GitHub Actions, EF migrations, TS strict, Tailwind 3, Serena + CodeGraph (read-only dev MCP, already active).
- **Rejected MCP:** filesystem/git/github (duplicate native + `gh`), **postgres/EF DB MCP (critical — would expose PHI + bypass tenant isolation/audit)**, playwright/browser (duplicates `agent-browser`). `mcp-connectors/` is a signed clinical **product** feature, never a dev MCP.
- **Agents:** the 4 `subagents/` retained + rebranded (done). **Skipped (your directive):** Dependabot, SCA, CodeQL, gitleaks, ESLint.
- **Retired skills:** the (now-deleted) root `skills/` Open Design content; the gitignored `.agents/.claude` variants `csharp-mstest/nunit/tunit`, `dotnet-upgrade`, `next-upgrade` should also be pruned locally (they contradict the xUnit-only, locked-stack rules) and the useful on-stack dev skills committed to a tracked path.

---

## Developer usage guide (post-cleanup)

- **Source of truth:** `CLAUDE.md` (authoritative) → `AGENTS.md`/`GEMINI.md` are pointers. UI contract: RC design system, `frontend/app/tokens.css`, both themes.
- **Build a feature:** use the `feature-dev` subagent; reuse `frontend/lib/api.ts` + the backend layering; add xUnit/Vitest tests; push and watch CI (`gh run watch`) — do **not** build the whole solution/frontend locally. Frontend/desktop changes ⇒ `pnpm release:desktop`.
- **Review:** the `code-reviewer` subagent enforces the safety boundaries (no auto-sign, `.ai-mark`, PHI policy, append-only audit, tenant isolation) and the locked stack.
- **Explore/tests:** `explorer` (Serena + CodeGraph first) and `test-runner` (single targeted test locally; CI is the gate).
- **Security:** `.github/instructions/security.instructions.md` now reflects the real auth stack; `docs/04-security/` for architecture.
