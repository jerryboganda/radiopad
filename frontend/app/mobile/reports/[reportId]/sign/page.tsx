import MobileSignClient from './MobileSignClient';

export const dynamicParams = false;

export function generateStaticParams(): Array<{ reportId: string }> {
  return [];
}

export default function MobileSignPage() {
  return <MobileSignClient />;
}
