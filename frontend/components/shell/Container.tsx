import type { ReactNode } from 'react';

export interface ContainerProps {
  children: ReactNode;
  /** Drop max-width for full-bleed pages (e.g. .split editors). */
  fluid?: boolean;
  className?: string;
}

export default function Container({ children, fluid, className }: ContainerProps) {
  const cls = ['rp-container', fluid ? 'fluid' : '', className].filter(Boolean).join(' ');
  return <div className={cls}>{children}</div>;
}
