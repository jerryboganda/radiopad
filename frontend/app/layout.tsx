import '@fontsource-variable/inter';
import './globals.css';
import './tokens.css';
import './motion.css';
import './radiopad.css';
import './shell.css';
import type { ReactNode } from 'react';
import type { Viewport } from 'next';
import ShellBridge from './ShellBridge';
import IntlBoundary from '@/components/IntlBoundary';
import AppShell from '@/components/shell/AppShell';
import { THEME_BOOTSTRAP_SCRIPT, THEME_COLORS } from '@/lib/theme';

export const metadata = {
  title: 'RadioPad — AI radiology reporting',
  description: 'AI-assisted radiology reporting. Radiologist remains the final authority.',
};

export const viewport: Viewport = {
  // Light is the first-run default (THEME-001); the pre-paint bootstrap
  // and lib/theme.ts rewrite this meta when the theme changes.
  themeColor: THEME_COLORS.light,
};

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    // suppressHydrationWarning: the pre-paint bootstrap may set
    // data-theme="dark" on <html> before React hydrates.
    <html lang="en" suppressHydrationWarning>
      <body>
        {/* Pre-paint theme bootstrap — no wrong-theme flash (THEME-006). */}
        <script dangerouslySetInnerHTML={{ __html: THEME_BOOTSTRAP_SCRIPT }} />
        <IntlBoundary>
          <AppShell>{children}</AppShell>
          <ShellBridge />
        </IntlBoundary>
      </body>
    </html>
  );
}
