import './globals.css';
import './radiopad.css';
import type { ReactNode } from 'react';
import ShellBridge from './ShellBridge';
import BillingStatusBanner from '@/components/BillingStatusBanner';
import DesktopStatusBanner from '@/components/DesktopStatusBanner';
import IntlBoundary from '@/components/IntlBoundary';
import Topbar from '@/components/Topbar';

export const metadata = {
  title: 'RadioPad — AI radiology reporting',
  description: 'AI-assisted radiology reporting. Radiologist remains the final authority.',
};

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    <html lang="en">
      <body>
        <IntlBoundary>
          <div className="app">
            <BillingStatusBanner />
            <Topbar />
            <DesktopStatusBanner />
            <main className="rp-main">{children}</main>
          </div>
          <ShellBridge />
        </IntlBoundary>
      </body>
    </html>
  );
}
