'use client';

import type { ReactNode } from 'react';
import { usePathname } from 'next/navigation';

/**
 * Replays a brief entrance animation on every route change by keying the
 * subtree on the current pathname (a key change remounts the node, which
 * restarts its CSS entrance animation). Dependency-free; reduced-motion is
 * honored by the global rule + `.rp-anim-*` parity in motion.css.
 *
 * Mounted once inside the app shell, around the page content.
 */
export default function PageTransition({ children }: { children: ReactNode }) {
  const pathname = usePathname();
  return (
    <div key={pathname ?? ''} className="rp-page-transition rp-anim-fade-in">
      {children}
    </div>
  );
}
