import RulebookDetailClient from './RulebookDetailClient';

export const dynamicParams = false;

export function generateStaticParams(): Array<{ id: string }> {
  return [];
}

export default function RulebookDetailPage() {
  return <RulebookDetailClient />;
}