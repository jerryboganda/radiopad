'use client';

import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';

type Slot = ReactNode | null;

interface PageActionsState {
  setActions: (node: Slot) => void;
  actions: Slot;
}

const Ctx = createContext<PageActionsState | null>(null);

export function PageActionsProvider({ children }: { children: ReactNode }) {
  const [actions, setActions] = useState<Slot>(null);
  const value = useMemo(() => ({ actions, setActions }), [actions]);
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

export function usePageActions(): PageActionsState {
  const ctx = useContext(Ctx);
  if (!ctx) throw new Error('usePageActions must be used inside <PageActionsProvider>');
  return ctx;
}

/** Render this anywhere on a page to inject controls into the topbar's action slot. */
export function PageActions({ children }: { children: ReactNode }) {
  const { setActions } = usePageActions();
  useEffect(() => {
    setActions(children);
    return () => setActions(null);
  }, [children, setActions]);
  return null;
}

export function PageActionsSlot() {
  const { actions } = usePageActions();
  if (!actions) return null;
  return <>{actions}</>;
}
