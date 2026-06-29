'use client';

import {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import Banner, { type BannerTone } from './Banner';

interface Toast {
  id: number;
  tone: BannerTone;
  title?: ReactNode;
  message?: ReactNode;
  duration: number;
}

export interface ToastInput {
  tone?: BannerTone;
  title?: ReactNode;
  message?: ReactNode;
  /** ms before auto-dismiss; 0 keeps it until manually dismissed. */
  duration?: number;
}

interface ToastApi {
  toast: (input: ToastInput) => number;
  dismiss: (id: number) => void;
}

const ToastContext = createContext<ToastApi | null>(null);

/**
 * App-wide toast notifications. Toasts slide in from the right, stack in a
 * fixed bottom-right region, and auto-dismiss (errors linger longer). Mounted
 * once near the shell root; consume via `useToast()`.
 */
export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);
  const idRef = useRef(0);

  const dismiss = useCallback((id: number) => {
    setToasts((prev) => prev.filter((t) => t.id !== id));
  }, []);

  const toast = useCallback(
    (input: ToastInput) => {
      const id = ++idRef.current;
      const tone = input.tone ?? 'info';
      const duration = input.duration ?? (tone === 'danger' ? 8000 : 4000);
      setToasts((prev) => [...prev, { id, tone, title: input.title, message: input.message, duration }]);
      if (duration > 0 && typeof window !== 'undefined') {
        window.setTimeout(() => dismiss(id), duration);
      }
      return id;
    },
    [dismiss],
  );

  const api = useMemo<ToastApi>(() => ({ toast, dismiss }), [toast, dismiss]);

  return (
    <ToastContext.Provider value={api}>
      {children}
      <div className="rp-toast-region" role="region" aria-label="Notifications">
        {toasts.map((t) => (
          <div key={t.id} className="rp-toast rp-anim-slide-right">
            <Banner tone={t.tone} title={t.title} onDismiss={() => dismiss(t.id)}>
              {t.message}
            </Banner>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}

export function useToast(): ToastApi {
  const ctx = useContext(ToastContext);
  if (!ctx) throw new Error('useToast must be used inside <ToastProvider>');
  return ctx;
}
