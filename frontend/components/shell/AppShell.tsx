'use client';

import type { ReactNode } from 'react';
import { usePathname } from 'next/navigation';
import Sidebar from './Sidebar';
import Topbar from './Topbar';
import MobileDrawerBackdrop from './MobileDrawerBackdrop';
import { ShellProvider, useShell } from './ShellContext';
import { PageActionsProvider } from './PageActionsSlot';
import AuthGate from './AuthGate';
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
    || pathname === '/pair' || pathname?.startsWith('/pair/');

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

  return (
    <ToastProvider>
      <ShellProvider>
        <PageActionsProvider>
          <AuthGate>
            <ShellRoot>{children}</ShellRoot>
          </AuthGate>
        </PageActionsProvider>
      </ShellProvider>
    </ToastProvider>
  );
}
