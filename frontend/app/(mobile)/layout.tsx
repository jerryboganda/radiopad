'use client';

import React from 'react';
import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { Mic, FileText } from 'lucide-react';

export default function MobileLayout({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();

  const navItems = [
    {
      label: 'Companion',
      href: '/companion',
      icon: Mic,
      isActive: pathname === '/companion' || pathname?.startsWith('/companion/'),
    },
    {
      label: 'Reporting',
      href: '/reporting',
      icon: FileText,
      isActive: pathname === '/reporting' || pathname?.startsWith('/reporting') || pathname?.startsWith('/mobile/reporting'),
    },
  ];

  return (
    <div className="rp-mobile-layout flex flex-col min-h-screen bg-[var(--bg-app,#0b0f17)] text-[var(--text,#e2e8f0)]">
      {/* Content View */}
      <div className="flex-1 w-full relative">
        {children}
      </div>

      {/* Bottom Navigation Tab Bar */}
      <nav
        aria-label="Mobile navigation"
        className="fixed bottom-0 left-0 right-0 z-40 bg-[var(--bg-panel,#131722)]/95 backdrop-blur-md border-t border-[var(--border,#262c40)] px-6 py-2 flex items-center justify-around shadow-lg"
      >
        {navItems.map((item) => {
          const Icon = item.icon;
          return (
            <Link
              key={item.href}
              href={item.href}
              aria-selected={item.isActive}
              className={`flex flex-col items-center justify-center gap-1 py-1 px-4 rounded-xl transition-all duration-200 ${
                item.isActive
                  ? 'text-blue-400 font-semibold scale-105'
                  : 'text-slate-400 hover:text-slate-200'
              }`}
            >
              <div
                className={`p-1.5 rounded-lg transition-colors ${
                  item.isActive ? 'bg-blue-600/20 text-blue-400' : 'bg-transparent'
                }`}
              >
                <Icon className="w-5 h-5" />
              </div>
              <span className="text-[11px] tracking-wide">{item.label}</span>
            </Link>
          );
        })}
      </nav>
    </div>
  );
}
