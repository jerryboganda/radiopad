import type { CapacitorConfig } from '@capacitor/cli';

const config: CapacitorConfig = {
  appId: 'com.radiopad.mobile',
  appName: 'RadioPad',
  // The mobile shell wraps the `mobile` surface build (companion: pairing +
  // dictation only), produced by `pnpm --filter @radiopad/frontend build:mobile`.
  webDir: '../frontend/out-mobile',
  bundledWebRuntime: false,
  server: {
    androidScheme: 'https',
    // PHI transport MUST be encrypted (HIPAA). Cleartext HTTP/WS is OFF by
    // default so production APKs cannot downgrade. Developers pointing the
    // companion at a desktop over plain http on the LAN can opt in for a
    // local build only via RADIOPAD_MOBILE_CLEARTEXT=1; never set it in CI.
    cleartext: process.env.RADIOPAD_MOBILE_CLEARTEXT === '1',
  },
  plugins: {
    SplashScreen: {
      launchShowDuration: 600,
      // RC canvas (light) — see frontend/app/tokens.css.
      backgroundColor: '#f5f8fb',
    },
    PushNotifications: {
      // PRD MOB-007 — request `alert` + `badge` + `sound` on first launch.
      // The native shell prompts the OS permission sheet via
      // `PushNotifications.requestPermissions()` (see frontend/lib/push.ts).
      presentationOptions: ['alert', 'badge', 'sound'],
    },
  },
};

export default config;
