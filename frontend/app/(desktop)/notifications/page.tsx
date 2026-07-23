import NotificationsPageClient from './NotificationsPageClient';

/**
 * NOTIF-001 — the personal notifications inbox (desktop reporting product only;
 * web admins use the topbar bell). Thin server entry that renders the client, the
 * same page.tsx ⁄ Client split the report editor uses.
 */
export default function NotificationsPage() {
  return <NotificationsPageClient />;
}
