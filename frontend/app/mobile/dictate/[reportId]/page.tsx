import MobileDictateClient from './MobileDictateClient';

export const dynamicParams = false;

export function generateStaticParams(): Array<{ reportId: string }> {
  return [];
}

export default function MobileDictatePage() {
  return <MobileDictateClient />;
}
