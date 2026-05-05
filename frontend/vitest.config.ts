import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import path from 'node:path';

// Vitest configuration for the locked Next.js 16 / React 18 frontend.
// Tests live in `__tests__/` and run under jsdom; the real backend is
// never contacted (fetch is mocked via `vi.fn()` in each test).
export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, '.'),
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    include: ['__tests__/**/*.test.{ts,tsx}'],
    setupFiles: ['./__tests__/setup.ts'],
    css: false,
  },
});
