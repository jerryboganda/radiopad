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
        <main className="rp-shell-content">{children}</main>
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
      <PageActionsProvider>
        <main className="rp-public-auth-content">{children}</main>
      </PageActionsProvider>
    );
  }

  return (
    <ShellProvider>
      <PageActionsProvider>
        <AuthGate>
          <ShellRoot>{children}</ShellRoot>
        </AuthGate>
      </PageActionsProvider>
    </ShellProvider>
  );
}
