'use client';

import type { ReactNode } from 'react';
import { usePathname } from 'next/navigation';
import Sidebar from './Sidebar';
import Topbar from './Topbar';
import MobileDrawerBackdrop from './MobileDrawerBackdrop';
import { ShellProvider, useShell } from './ShellContext';
import { PageActionsProvider } from './PageActionsSlot';
import AuthGate from './AuthGate';
import WebAdminGate from './WebAdminGate';
import { isWebSurface } from '@/lib/surface';
import BillingStatusBanner from '@/components/BillingStatusBanner';
import DesktopStatusBanner from '@/components/DesktopStatusBanner';
import PageTransition from '@/components/ui/PageTransition';
import { ToastProvider } from '@/components/ui/ToastProvider';

function ShellRoot({ children }: { children: ReactNode }) {
  const { collapsed } = useShell();
  return (
    <div className={`rp-shell ${collapsed ? 'collapsed' : ''}`}>
      <Sidebar />
      <MobileDrawerBackdrop />
      <div className="rp-shell-main">
        <BillingStatusBanner />
        <Topbar />
        <DesktopStatusBanner />
        <main className="rp-shell-content">
          <PageTransition>{children}</PageTransition>
        </main>
      </div>
    </div>
  );
}

export default function AppShell({ children }: { children: ReactNode }) {
  const pathname = usePathname();
  const publicAuthRoute = pathname === '/login' || pathname?.startsWith('/login/')
    || pathname === '/register' || pathname?.startsWith('/register/')
    || pathname === '/pair' || pathname?.startsWith('/pair/')
    // The mobile companion pairs by scanning the desktop QR, which carries its
    // OWN short-lived bearer — the phone is intentionally signed out until it
    // scans. Gating /companion behind AuthGate would bounce it to /login and it
    // could never reach the scanner. It renders its own pair/scan chrome.
    || pathname === '/companion' || pathname?.startsWith('/companion/');

  if (publicAuthRoute) {
    return (
      <ToastProvider>
        <PageActionsProvider>
          <main className="rp-public-auth-content">
            <PageTransition>{children}</PageTransition>
          </main>
        </PageActionsProvider>
      </ToastProvider>
    );
  }

  // On the web (master-admin) surface, clinical-only users are shown the
  // desktop-app notice instead of the shell. `isWebSurface` is an inlined build
  // constant, so this branch is compiled away entirely on desktop/mobile.
  const shell = isWebSurface ? (
    <WebAdminGate>
      <ShellRoot>{children}</ShellRoot>
    </WebAdminGate>
  ) : (
    <ShellRoot>{children}</ShellRoot>
  );

  return (
    <ToastProvider>
      <ShellProvider>
        <PageActionsProvider>
          <AuthGate>{shell}</AuthGate>
        </PageActionsProvider>
      </ShellProvider>
    </ToastProvider>
  );
}
