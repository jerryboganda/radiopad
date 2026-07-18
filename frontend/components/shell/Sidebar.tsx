'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { useEffect } from 'react';
import { navGroups, isActive, type NavItem } from './nav.config';
import { useShell } from './ShellContext';
import { usePermissions } from '@/lib/permissions';
import { surfaceAllows } from '@/lib/surface';
import { ChevronLeft, ChevronRight } from 'lucide-react';

export default function Sidebar() {
  const tNav = useTranslations('nav');
  const tGroups = useTranslations('nav.groups');
  const tBar = useTranslations('topbar');
  const pathname = usePathname() ?? '/';
  const { collapsed, toggleCollapsed, drawerOpen, closeDrawer } = useShell();
  const { can, role, loading: permsLoading } = usePermissions();

  // Show every item until the permission set resolves (avoids a flash of an
  // empty sidebar), then hide items whose required permission (or role, for
  // role-gated items like UBAG Hub) the user lacks.
  // The backend still enforces RBAC; this is purely which links we surface.
  const visible = (it: NavItem) =>
    permsLoading ||
    ((!it.permission || can(it.permission)) &&
      (!it.roles || (role !== null && it.roles.includes(role))));
  // Scope items to the surface this bundle was built for (item tag overrides
  // the group tag; neither → shared). Applied alongside the RBAC filter — the
  // backend still enforces both.
  const groups = navGroups
    .map((g) => ({
      ...g,
      items: g.items.filter(
        (it) => visible(it) && surfaceAllows(it.surfaces ?? g.surfaces),
      ),
    }))
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
                  title={tNav(item.labelKey)}
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
        {/* RC sidebar: labeled Collapse control pinned to the footer. */}
        <button
          type="button"
          className="rp-sidebar-collapse"
          aria-label={collapsed ? tBar('expandSidebar') : tBar('collapseSidebar')}
          aria-expanded={!collapsed}
          onClick={toggleCollapsed}
        >
          <span className="rp-sidebar-icon" aria-hidden>
            {collapsed ? <ChevronRight /> : <ChevronLeft />}
          </span>
          <span className="rp-sidebar-label">{tBar('collapse')}</span>
        </button>
      </div>
    </aside>
  );
}
