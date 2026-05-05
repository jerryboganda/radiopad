import type { ReactNode } from 'react';

export const dynamicParams = false;

export function generateStaticParams(): Array<{ id: string }> {
  return [];
}

export default function RulebookLayout({ children }: { children: ReactNode }) {
  return children;
}