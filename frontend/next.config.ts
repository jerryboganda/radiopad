import type { NextConfig } from 'next';

const config: NextConfig = {
  // Static export so the same bundle ships into Tauri (desktop) and
  // Capacitor (iOS/Android) without a Node server.
  output: 'export',
  trailingSlash: true,
  images: { unoptimized: true },
  // Next's dev server blocks cross-origin dev/HMR requests by default — a
  // request for /_next/webpack-hmr from anything but localhost is dropped,
  // which throws during client bundle init and leaves the page stuck on its
  // initial (pre-hydration) render, e.g. the companion splash never unmounts.
  // Needed to open `next dev` from a phone/other device over the LAN.
  // RADIOPAD_DEV_LAN_ORIGIN overrides/adds a host (e.g. when your LAN IP
  // changes); comma-separate multiple hosts if needed.
  allowedDevOrigins: (process.env.RADIOPAD_DEV_LAN_ORIGIN ?? '192.168.18.40')
    .split(',')
    .map((h) => h.trim())
    .filter(Boolean),
  // Which specialised surface this build targets: desktop (full reporting
  // product), web (master-admin only), or mobile (dictation companion).
  // Selected by `scripts/build-surface.mjs`; inlined here so client code can
  // branch on it (see `lib/surface.ts`). Defaults to the full desktop app.
  env: {
    RADIOPAD_SURFACE: process.env.RADIOPAD_SURFACE ?? 'desktop',
    // Dev-only: the SSE event stream (`/api/events/stream`) is long-lived and
    // Next's dev rewrite proxy buffers/mangles streaming responses (fetch to a
    // remote origin surfaces as a tight ECONNRESET reconnect loop that can
    // starve the Turbopack compiler). Give the client the same target the
    // rewrite proxy would use so it can fetch this ONE endpoint directly,
    // bypassing the proxy — CORS already allow-lists localhost:3000
    // (Program.cs). Every other call still goes through the same-origin
    // proxy as before. Unset in production builds (output: 'export'), so
    // `apiBase()` falls back to its normal NEXT_PUBLIC_API_BASE / relative
    // behaviour there, unchanged.
    RADIOPAD_DEV_STREAM_BASE:
      process.env.NODE_ENV === 'production'
        ? ''
        : (process.env.RADIOPAD_DEV_API_PROXY || 'http://127.0.0.1:7457').replace(/\/+$/, ''),
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
