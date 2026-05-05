# RadioPad Mobile (Capacitor 6)

Wraps the static export of the Next.js frontend for iOS and Android.

## Prerequisites

- Node + pnpm
- Android Studio (for Android) and/or Xcode (for iOS)
- The Capacitor CLI (`pnpm install` here installs it locally as a dev dep)

## First-time setup

```powershell
cd mobile
pnpm install
pnpm exec cap add android
pnpm exec cap add ios       # macOS only
```

## Push notifications (PRD MOB-007)

The native shell uses [`@capacitor/push-notifications`](https://capacitorjs.com/docs/apis/push-notifications). Tokens are
delivered to the backend via `POST /api/push/devices` and dispatched by the
RadioPad API directly to APNs (token-auth) and FCM HTTP v1.

Required environment variables on the **backend host** (never on the device):

| Variable | Purpose |
| --- | --- |
| `RADIOPAD_APNS_KEY_P8` | Absolute path to the Apple `.p8` signing key. |
| `RADIOPAD_APNS_KEY_ID` | Apple key id (10-char). |
| `RADIOPAD_APNS_TEAM_ID` | Apple developer team id. |
| `RADIOPAD_APNS_BUNDLE_ID` | Bundle id (matches `appId` in `capacitor.config.ts`). |
| `RADIOPAD_FCM_PROJECT_ID` | Firebase project id. |
| `RADIOPAD_FCM_SERVICE_ACCOUNT_JSON` | Absolute path to the FCM service-account JSON. |

Notification payloads are intentionally generic (`title: "RadioPad"`,
`body: "You have a new notification"`); routing is encoded in `data.kind` +
`data.entityId`. **Never** include PHI in a push payload.

Native signing requirements:

- **iOS:** an APNs Auth Key (`.p8`) tied to the Apple Developer team. Add the
  Push Notifications capability in Xcode (`Signing & Capabilities`).
- **Android:** add `google-services.json` to `android/app/`. The Firebase
  project must use FCM HTTP v1 (legacy server keys are not supported).

## Biometric unlock (PRD MOB-008)

The mobile shell uses
[`@aparajita/capacitor-biometric-auth`](https://github.com/aparajita/capacitor-biometric-auth)
to gate release of the cached bearer token. Toggle the lock from the Settings
screen — when enabled, RadioPad prompts Face ID / Touch ID / Android biometric
on every launch before the API client is allowed to attach `Authorization`.

iOS: add `NSFaceIDUsageDescription` to `ios/App/App/Info.plist`. Android:
biometric availability is auto-detected; no additional config is required for
API 28+.

## Build & sync

```powershell
pnpm sync       # builds the web bundle and copies it into the native projects
pnpm android    # opens Android Studio
pnpm ios        # opens Xcode (macOS only)
```

## Dictation (PRD MOB / iter-36)

The mobile shell ships three dedicated touch pages (served by the same Next.js
frontend that the WebView wraps):

- `/mobile/dictate/[reportId]` — Web Speech API dictation with a localStorage
  draft buffer so a network drop never costs spoken notes. Uses
  [`@capacitor-community/speech-recognition`](https://github.com/capacitor-community/speech-recognition)
  on native (added to `package.json`); falls back gracefully on browsers that
  do not expose `window.SpeechRecognition`.
- `/mobile/reports/[reportId]/edit` — collapsible per-section editor.
- `/mobile/reports/[reportId]/sign` — acknowledgement-and-export screen.
  RadioPad never auto-signs; the radiologist still signs in their RIS/EHR.

After `pnpm exec cap add ios|android`, configure the platform permissions:

- **iOS** — add to `ios/App/App/Info.plist`:
  - `NSSpeechRecognitionUsageDescription` — "RadioPad converts your dictation
    into report findings on-device. Your voice is not stored."
  - `NSMicrophoneUsageDescription` — "RadioPad needs the microphone to
    capture dictation."
- **Android** — add to `android/app/src/main/AndroidManifest.xml`:
  - `<uses-permission android:name="android.permission.RECORD_AUDIO"/>`

The page calls `SpeechRecognition.requestPermissions()` from the community
plugin on native; the web fallback uses the browser's standard
`navigator.mediaDevices` permission flow.

## Design lock

The mobile shell renders the same Next.js frontend as web/desktop — colours,
typography, and component classes are governed by `frontend/app/globals.css`
and `frontend/app/radiopad.css`. Use the splash background `#faf9f7` to match.
