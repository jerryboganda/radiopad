# RadioPad Desktop — user guide

The desktop app is a Tauri 2 shell that loads the same static frontend used on
the web and connects to the **hosted RadioPad service** for all of your data —
sign-in, reports, AI, and settings. You do **not** run a local server. The only
thing that runs on your machine is the **on-device dictation engine**, so your
spoken audio is transcribed locally and never leaves the computer.

## Install

Pre-built installers for Windows (`.msi`), macOS (`.dmg`), and Linux
(`.deb` / `.AppImage`) are produced by CI and published to the releases page.

## First run

1. Launch the **RadioPad** app.
2. The window opens at the **sign-in** screen. Sign in with your RadioPad
   account (password + authenticator code, and biometric/passkey if enrolled).
3. You're taken to the dashboard, working against the live service.
4. The **first time you use dictation**, the on-device speech engine downloads
   its model in the background (a one-time step). Until it finishes, dictation
   shows a brief "engine is starting up" message — just try again in a moment.

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
| Can't sign in / pages won't load | No internet, or the hosted service is unreachable | Check your connection; the desktop needs to reach `radiopad.polytronx.com` |
| Dictation: "engine is starting up" | On-device model still downloading on first run, or the machine can't reach the model host | Wait a moment and retry; if it persists, your network may be blocking the one-time model download |
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
