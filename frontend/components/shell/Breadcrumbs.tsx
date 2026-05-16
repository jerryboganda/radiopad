import Link from 'next/link';
import type { ReactNode } from 'react';

export interface BreadcrumbItem {
  label: string;
  href?: string;
}

export default function Breadcrumbs({ items }: { items: BreadcrumbItem[] }): ReactNode {
  if (!items.length) return null;
  return (
    <nav className="rp-breadcrumbs" aria-label="Breadcrumb">
      {items.map((item, i) => {
        const last = i === items.length - 1;
        return (
          <span key={`${item.label}-${i}`} style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
            {i > 0 && <span className="rp-breadcrumb-sep" aria-hidden>/</span>}
            {item.href && !last ? (
              <Link href={item.href}>{item.label}</Link>
            ) : (
              <span className="rp-breadcrumb-current" aria-current={last ? 'page' : undefined}>{item.label}</span>
            )}
          </span>
        );
      })}
    </nav>
  );
}
