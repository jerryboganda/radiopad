import MobileEditClient from './MobileEditClient';

export const dynamicParams = false;

export function generateStaticParams(): Array<{ reportId: string }> {
  return [{ reportId: '__static_export_placeholder__' }];
}

export default function MobileEditPage() {
  return <MobileEditClient />;
}
