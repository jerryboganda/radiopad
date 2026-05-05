import RulebookDetailClient from './RulebookDetailClient';

export const dynamicParams = false;

export function generateStaticParams(): Array<{ id: string }> {
  return [{ id: '__static_export_placeholder__' }];
}

export default function RulebookDetailPage() {
  return <RulebookDetailClient />;
}