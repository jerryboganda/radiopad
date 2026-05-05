import type { NextConfig } from 'next';

const config: NextConfig = {
  // Static export so the same bundle ships into Tauri (desktop) and
  // Capacitor (iOS/Android) without a Node server.
  output: 'export',
  trailingSlash: true,
  images: { unoptimized: true },
  // Dev rewrites: when running `next dev`, proxy /api/* to the ASP.NET
  // backend so the SPA can call REST endpoints without CORS friction.
  // In production (`output: 'export'`) the frontend talks to the backend
  // through `NEXT_PUBLIC_API_BASE`.
  async rewrites() {
    if (process.env.NODE_ENV === 'production') return [];
    return [
      { source: '/api/:path*', destination: 'http://127.0.0.1:7457/api/:path*' },
    ];
  },
};

export default config;
