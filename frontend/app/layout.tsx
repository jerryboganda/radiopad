import './globals.css';
import './radiopad.css';
import './shell.css';
import type { ReactNode } from 'react';
import ShellBridge from './ShellBridge';
import IntlBoundary from '@/components/IntlBoundary';
import AppShell from '@/components/shell/AppShell';
import DictationOverlay from '@/components/dictation/DictationOverlay';

export const metadata = {
  title: 'RadioPad — AI radiology reporting',
  description: 'AI-assisted radiology reporting. Radiologist remains the final authority.',
};

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    <html lang="en">
      <body>
        <IntlBoundary>
          <AppShell>{children}</AppShell>
          <ShellBridge />
          <DictationOverlay />
        </IntlBoundary>
      </body>
    </html>
  );
}
