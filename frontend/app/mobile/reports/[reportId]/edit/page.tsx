import MobileEditClient from './MobileEditClient';

export const dynamicParams = false;

export function generateStaticParams(): Array<{ reportId: string }> {
  return [];
}

export default function MobileEditPage() {
  return <MobileEditClient />;
}
