'use client';

import React from 'react';
import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { Mic, FileText } from 'lucide-react';

/**
 * Mobile ships two destinations (frontend/CLAUDE.md §"three specialised surfaces"):
 * - Companion — pairing + voice dictation into a LIVE desktop session (public route,
 *   carries only a short-lived QR pairing token).
 * - Reporting — standalone report creation + multi-take audio dictation with a real
 *   signed-in session (goes through AuthGate → /login like every other route), which
 *   later hands off to the desktop's Positive Findings tab for AI transcription.
 */
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
      isActive: pathname === '/reporting' || pathname?.startsWith('/reporting/'),
    },
  ];

  return (
    <div className="rp-mobile-layout">
      {/* Content View */}
      <div className="rp-mobile-layout-content">
        {children}
      </div>

      {/* Bottom Navigation Tab Bar */}
      <nav aria-label="Mobile navigation" className="rp-mobile-nav">
        {navItems.map((item) => {
          const Icon = item.icon;
          return (
            <Link
              key={item.href}
              href={item.href}
              aria-selected={item.isActive}
              className={`rp-mobile-nav-item${item.isActive ? ' active' : ''}`}
            >
              <div className="rp-mobile-nav-icon">
                <Icon size={20} aria-hidden />
              </div>
              <span className="rp-mobile-nav-label">{item.label}</span>
            </Link>
          );
        })}
      </nav>
    </div>
  );
}
