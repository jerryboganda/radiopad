import type { NextConfig } from 'next';

const config: NextConfig = {
  // Static export so the same bundle ships into Tauri (desktop) and
  // Capacitor (iOS/Android) without a Node server.
  output: 'export',
  trailingSlash: true,
  images: { unoptimized: true },
  // Which specialised surface this build targets: desktop (full reporting
  // product), web (master-admin only), or mobile (dictation companion).
  // Selected by `scripts/build-surface.mjs`; inlined here so client code can
  // branch on it (see `lib/surface.ts`). Defaults to the full desktop app.
  env: {
    RADIOPAD_SURFACE: process.env.RADIOPAD_SURFACE ?? 'desktop',
  },
  // Dev rewrites: when running `next dev`, proxy /api/* to the ASP.NET
  // backend so the SPA can call REST endpoints without CORS friction.
  // In production (`output: 'export'`) the frontend talks to the backend
  // through `NEXT_PUBLIC_API_BASE`.
  //
  // `RADIOPAD_DEV_API_PROXY` retargets that proxy at a REMOTE api (e.g. the
  // hosted https://admin.radiopadstudio.com) without touching this file. Prefer
  // it over pointing `NEXT_PUBLIC_API_BASE` at a remote host in dev: the base
  // makes the BROWSER issue cross-origin calls, which depend on the server's
  // CORS allow-list and are silently killed by privacy/ad-blocking extensions
  // (they surface as an unhelpful "Could not reach the RadioPad server"). Going
  // through this proxy keeps every call same-origin — the hop to the remote api
  // happens server-side, where none of that applies. Defaults to the local
  // sidecar, so the standard local-backend workflow is unchanged.
  async rewrites() {
    if (process.env.NODE_ENV === 'production') return [];
    const target = (process.env.RADIOPAD_DEV_API_PROXY || 'http://127.0.0.1:7457').replace(/\/+$/, '');
    return [
      { source: '/api/:path*', destination: `${target}/api/:path*` },
    ];
  },
};

export default config;
