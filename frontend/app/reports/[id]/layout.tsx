import type { ReactNode } from 'react';

export const dynamicParams = false;

export function generateStaticParams(): Array<{ id: string }> {
  return [];
}

export default function ReportLayout({ children }: { children: ReactNode }) {
  return children;
}