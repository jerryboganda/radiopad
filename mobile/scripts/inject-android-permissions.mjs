// Inject the Android permissions the companion's WebView microphone needs.
//
// The companion phone captures dictation audio with the WebView's
// `navigator.mediaDevices.getUserMedia({ audio: true })`. On Android, that call
// is gated by Capacitor's `BridgeWebChromeClient.onPermissionRequest`, which —
// for `android.webkit.resource.AUDIO_CAPTURE` — asks the OS to grant BOTH
// `RECORD_AUDIO` *and* `MODIFY_AUDIO_SETTINGS` and denies the whole WebView
// request if EITHER is not granted (see @capacitor/android
// BridgeWebChromeClient.java: it builds {MODIFY_AUDIO_SETTINGS, RECORD_AUDIO}
// and calls request.deny() unless every one comes back granted).
//
// A runtime-permission request for a permission that is NOT declared in the
// manifest returns "denied" immediately, with no dialog. The
// @capacitor-community/speech-recognition plugin merges `RECORD_AUDIO`, but
// nothing declares `MODIFY_AUDIO_SETTINGS`, and Capacitor's default app
// template ships only `INTERNET`. So audio capture was being denied and
// `getUserMedia` rejected with `NotAllowedError` → the phone showed the
// generic "Could not start the microphone."
//
// The Android project is generated fresh on CI (`npx cap add android`), so
// there is no committed manifest to edit. This script idempotently injects the
// required <uses-permission> entries into the generated manifest. It runs after
// `npx cap sync android` in mobile-bundle.yml and after `cap sync` in the local
// `pnpm sync` (see mobile/package.json), so CI builds and local `cap open
// android` both get them.

import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
// Defaults to the Capacitor-generated app manifest; an explicit path may be
// passed as the first CLI arg (used by the smoke test).
const manifestPath = process.argv[2]
  ? resolve(process.argv[2])
  : resolve(here, '..', 'android', 'app', 'src', 'main', 'AndroidManifest.xml');

// Permissions the WebView microphone path requires. RECORD_AUDIO is also merged
// by the speech-recognition plugin; declaring it here too is harmless (the
// manifest merger de-dupes identical <uses-permission> entries) and keeps the
// requirement self-documenting if that plugin is ever removed.
const REQUIRED = [
  'android.permission.RECORD_AUDIO',
  'android.permission.MODIFY_AUDIO_SETTINGS',
];

if (!existsSync(manifestPath)) {
  // The android platform isn't present in this checkout (e.g. iOS-only dev or a
  // fresh clone before `cap add android`). Nothing to patch — warn, don't fail,
  // so `pnpm sync` stays usable. CI asserts the permission separately after the
  // android project is generated, so a genuinely broken build still fails loudly.
  console.warn(
    `[inject-android-permissions] No android manifest at ${manifestPath} — skipping ` +
    '(run `npx cap add android` first if you meant to build Android).',
  );
  process.exit(0);
}

const original = readFileSync(manifestPath, 'utf8');

const missing = REQUIRED.filter((perm) => !original.includes(`android:name="${perm}"`));
if (missing.length === 0) {
  console.log('[inject-android-permissions] All required permissions already present — no change.');
  process.exit(0);
}

const closing = '</manifest>';
if (!original.includes(closing)) {
  console.error('[inject-android-permissions] Could not find </manifest> — manifest shape unexpected.');
  process.exit(1);
}

const block = missing.map((perm) => `    <uses-permission android:name="${perm}" />`).join('\n');
const patched = original.replace(closing, `${block}\n${closing}`);
writeFileSync(manifestPath, patched);

console.log(`[inject-android-permissions] Added: ${missing.join(', ')}`);
