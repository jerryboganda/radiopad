import type { ReactNode } from 'react';

export const dynamicParams = false;

export function generateStaticParams(): Array<{ reportId: string }> {
  return [];
}

export default function MobileDictateLayout({ children }: { children: ReactNode }) {
  return children;
}