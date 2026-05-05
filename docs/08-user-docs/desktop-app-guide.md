# RadioPad Desktop — user guide

The desktop app is a Tauri 2 shell that loads the same static frontend used
on the web and talks to a locally running RadioPad backend at
`http://127.0.0.1:7457`.

## Install

Pre-built installers for Windows (`.msi`), macOS (`.dmg`), and Linux
(`.deb` / `.AppImage`) are produced by `cargo tauri build` and published to
the releases page.

## First run

1. Launch the **RadioPad** app.
2. The window opens at the dashboard. If the backend is not yet running,
   the dashboard shows an amber banner with the exact error.
3. Start the backend:
   - **Windows:** double-click the bundled `RadioPad.Api.exe`, or
     `dotnet run --project backend/RadioPad.Api/src/RadioPad.Api` from a dev
     checkout.
   - **macOS / Linux:** `./RadioPad.Api` from the install dir.
4. Refresh the dashboard. It auto-recovers when the backend is reachable.

## Hotkey

`Ctrl + Shift + R` (Windows / Linux) or `⌃ + ⇧ + R` (macOS) brings the
RadioPad window forward from any application.

## Secure clipboard

When you copy an accession number or any field that may contain PHI from
within RadioPad, the app uses a secure-clipboard command that **clears the
clipboard after a short TTL**. This avoids leaving PHI in the OS clipboard
for paste targets you didn't intend.

## Troubleshooting

| Symptom | Likely cause | Fix |
| ------- | ------------ | --- |
| Amber banner: "Backend not reachable" | API not running | Start the backend (see above) |
| AI provider greyed out | Provider not enabled or no API key | Open **Providers** and toggle / set the key |
| AI button is disabled | The current rulebook is not approved | Open **Rulebooks** and approve a tested rulebook |
| Cannot acknowledge a report | Blocker-class findings present | Resolve every blocker in the validation panel |

## Data location

| OS | Path |
| -- | ---- |
| Windows | `%APPDATA%\RadioPad\` (config) ; `%LOCALAPPDATA%\RadioPad\` (db) |
| macOS | `~/Library/Application Support/RadioPad/` |
| Linux | `~/.config/RadioPad/` |

## Privacy

- All report data stays on the local machine unless you explicitly export
  FHIR or text.
- AI calls follow the tenant provider registry: PHI is blocked unless the
  destination provider is `PhiApproved` or `LocalOnly`.
