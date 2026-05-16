'use client';

import { useShell } from './ShellContext';

export default function MobileDrawerBackdrop() {
  const { drawerOpen, closeDrawer } = useShell();
  return (
    <div
      className={`rp-drawer-backdrop ${drawerOpen ? 'open' : ''}`}
      onClick={closeDrawer}
      aria-hidden={!drawerOpen}
    />
  );
}
