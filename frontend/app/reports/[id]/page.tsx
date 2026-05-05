import ReportClient from './ReportClient';

export const dynamicParams = false;

export function generateStaticParams(): Array<{ id: string }> {
  return [];
}

export default function ReportPage() {
  return <ReportClient />;
}
