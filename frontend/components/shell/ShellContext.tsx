'use client';

import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';

interface ShellState {
  collapsed: boolean;
  toggleCollapsed: () => void;
  drawerOpen: boolean;
  openDrawer: () => void;
  closeDrawer: () => void;
}

const ShellContext = createContext<ShellState | null>(null);

const STORAGE_KEY = 'rp-shell-collapsed';

export function ShellProvider({ children }: { children: ReactNode }) {
  const [collapsed, setCollapsed] = useState(false);
  const [drawerOpen, setDrawerOpen] = useState(false);

  useEffect(() => {
    if (typeof window === 'undefined') return;
    try {
      setCollapsed(window.localStorage.getItem(STORAGE_KEY) === '1');
    } catch {
      /* ignore */
    }
  }, []);

  const toggleCollapsed = useCallback(() => {
    setCollapsed((prev) => {
      const next = !prev;
      try {
        window.localStorage.setItem(STORAGE_KEY, next ? '1' : '0');
      } catch {
        /* ignore */
      }
      return next;
    });
  }, []);

  const openDrawer = useCallback(() => setDrawerOpen(true), []);
  const closeDrawer = useCallback(() => setDrawerOpen(false), []);

  // Close drawer on route change is handled by page, but also on Escape.
  useEffect(() => {
    if (!drawerOpen) return;
    const handler = (e: KeyboardEvent) => {
      if (e.key === 'Escape') closeDrawer();
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [drawerOpen, closeDrawer]);

  const value = useMemo<ShellState>(
    () => ({ collapsed, toggleCollapsed, drawerOpen, openDrawer, closeDrawer }),
    [collapsed, toggleCollapsed, drawerOpen, openDrawer, closeDrawer],
  );

  return <ShellContext.Provider value={value}>{children}</ShellContext.Provider>;
}

export function useShell(): ShellState {
  const ctx = useContext(ShellContext);
  if (!ctx) throw new Error('useShell must be used inside <ShellProvider>');
  return ctx;
}
