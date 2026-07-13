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
    // In dev, point to a desktop running the API + Next.js. In prod the
    // backend will be reached over the institution's VPN/HTTPS endpoint.
    cleartext: true,
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
