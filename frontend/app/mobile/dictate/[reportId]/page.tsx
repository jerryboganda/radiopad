import MobileDictateClient from './MobileDictateClient';

export const dynamicParams = false;

export function generateStaticParams(): Array<{ reportId: string }> {
  return [{ reportId: '__static_export_placeholder__' }];
}

export default function MobileDictatePage() {
  return <MobileDictateClient />;
}
