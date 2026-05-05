# Desktop Cloud Build

**Status:** Current  **Owner:** Release Engineering  **Last Updated:** 2026-05-05

RadioPad can build unsigned Windows desktop test installers on GitHub-hosted
Windows runners. This is the desktop equivalent of an EAS-style cloud build:
push the repository, let GitHub Actions run, then download the artifact.

## What Was Added

- Workflow: `.github/workflows/desktop-windows-test-build.yml`
- Build script: `scripts/build-radiopad-desktop-windows.ps1`

The workflow runs automatically when relevant files are pushed to `main` or a
`desktop-cloud-build/*` branch. It can also be started manually from the GitHub
Actions tab.

## Requirements

- GitHub Actions must be enabled for the repository or enterprise account.
- No signing secrets are required for this unsigned test build.
- The pushed repository must include the RadioPad project folders, not only the
  legacy Open Design files:
  - `backend/`
  - `frontend/`
  - `desktop/`
  - `mobile/`
  - `openapi/`
  - `rulebooks/`
  - `templates/`
  - `.github/workflows/desktop-windows-test-build.yml`
  - `package.json`, `pnpm-workspace.yaml`, `pnpm-lock.yaml`

## Automatic Build Flow

1. Push the project to GitHub. For a guided one-command path, run:

  ```powershell
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\prepare-desktop-cloud-build-commit.ps1 -Commit -Push -UseGenericIdentity
  ```

  To use your own author identity instead, configure Git first or omit `-UseGenericIdentity` and follow Git's prompt if it asks for `user.name` and `user.email`.

  ```powershell
  git config user.name "Your Name"
  git config user.email "you@example.com"
  ```

2. GitHub Actions starts `desktop-windows-test-build` automatically.
3. Open the workflow run in the Actions tab.
4. Download the `radiopad-windows-unsigned-*` artifact.
5. Extract the artifact and run the `.msi` or setup `.exe` on a Windows 10/11
   test machine.

The artifact is retained for 14 days.

## Manual Build Flow

1. Open the repository on GitHub.
2. Go to Actions.
3. Select `desktop-windows-test-build`.
4. Select Run workflow.
5. Keep `frozen-lockfile` off for ad-hoc test builds unless the lockfile has
   been regenerated and committed from a Node-equipped machine.

## What The Workflow Does

1. Checks out the repo.
2. Installs Node 20 and pnpm 9.15.9.
3. Installs .NET 8.
4. Installs stable Rust.
5. Publishes the ASP.NET Core API as a self-contained Windows sidecar.
6. Builds the Next.js static export under `frontend/out`.
7. Builds the Tauri Windows MSI/NSIS bundle.
8. Uploads the installer artifact.

## Windows 8 Note

The current RadioPad desktop stack is Tauri 2 plus ASP.NET Core/.NET 8.
Windows 8 and Windows 8.1 are not supported targets for this stack. Use Windows
10 or Windows 11 for end-to-end desktop testing.

## Troubleshooting

- If the helper script asks for confirmation, type `YES` after checking the
  displayed GitHub remote.
- If no workflow appears, confirm Actions are enabled in the repository or
  enterprise settings.
- If the workflow says files are missing, confirm the full RadioPad project
  folders were committed and pushed.
- If `pnpm install --frozen-lockfile` fails, rerun with `frozen-lockfile` off or
  regenerate and commit `pnpm-lock.yaml` from a machine with Node/pnpm.
- If SmartScreen warns on install, that is expected for unsigned test builds.
  Production release builds must use the signed release workflows.