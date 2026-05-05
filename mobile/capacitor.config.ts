import type { CapacitorConfig } from '@capacitor/cli';

const config: CapacitorConfig = {
  appId: 'com.radiopad.mobile',
  appName: 'RadioPad',
  webDir: '../frontend/out',
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
      backgroundColor: '#faf9f7',
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
