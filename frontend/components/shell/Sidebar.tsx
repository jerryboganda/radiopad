'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { useEffect } from 'react';
import { navGroups, isActive } from './nav.config';
import { useShell } from './ShellContext';
import ProfileMenu from './ProfileMenu';
import { usePermissions, type PermissionKey } from '@/lib/permissions';

export default function Sidebar() {
  const tNav = useTranslations('nav');
  const tGroups = useTranslations('nav.groups');
  const tBar = useTranslations('topbar');
  const pathname = usePathname() ?? '/';
  const { collapsed, toggleCollapsed, drawerOpen, closeDrawer } = useShell();
  const { can, loading: permsLoading } = usePermissions();

  // Show every item until the permission set resolves (avoids a flash of an
  // empty sidebar), then hide items whose required permission the user lacks.
  // The backend still enforces RBAC; this is purely which links we surface.
  const visible = (permission?: PermissionKey) =>
    permsLoading || !permission || can(permission);
  const groups = navGroups
    .map((g) => ({ ...g, items: g.items.filter((it) => visible(it.permission)) }))
    .filter((g) => g.items.length > 0);

  // Close mobile drawer on route change.
  useEffect(() => {
    if (drawerOpen) closeDrawer();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pathname]);

  return (
    <aside
      className={`rp-sidebar ${drawerOpen ? 'open' : ''}`}
      aria-label={tBar('primaryNav')}
      data-collapsed={collapsed ? 'true' : 'false'}
    >
      <Link href="/" className="rp-sidebar-brand">
        <span className="brand-mark" aria-hidden>
          <span className="brand-mark-letter">R</span>
        </span>
        <span className="rp-sidebar-brand-text">
          <span className="rp-sidebar-brand-title">{tBar('title')}</span>
          <span className="rp-sidebar-brand-meta">{tBar('tagline')}</span>
        </span>
      </Link>

      <button
        type="button"
        className="rp-sidebar-collapse-btn"
        aria-label={collapsed ? tBar('expandSidebar') : tBar('collapseSidebar')}
        aria-expanded={!collapsed}
        onClick={toggleCollapsed}
      >
        <span aria-hidden>{collapsed ? '›' : '‹'}</span>
      </button>

      <nav className="rp-sidebar-nav">
        {groups.map((group) => (
          <div key={group.labelKey} className="rp-sidebar-group">
            <div className="rp-sidebar-group-label">{tGroups(group.labelKey)}</div>
            {group.items.map((item) => {
              const active = isActive(pathname, item);
              const Icon = item.icon;
              return (
                <Link
                  key={item.href}
                  href={item.href}
                  className={`rp-sidebar-item ${active ? 'active' : ''}`}
                  aria-current={active ? 'page' : undefined}
                >
                  <span className="rp-sidebar-icon">
                    <Icon />
                  </span>
                  <span className="rp-sidebar-label">{tNav(item.labelKey)}</span>
                </Link>
              );
            })}
          </div>
        ))}
      </nav>

      <div className="rp-sidebar-footer">
        <ProfileMenu />
      </div>
    </aside>
  );
}
