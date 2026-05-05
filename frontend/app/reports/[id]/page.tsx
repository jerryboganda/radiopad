import ReportClient from './ReportClient';

export const dynamicParams = false;

export function generateStaticParams(): Array<{ id: string }> {
  return [{ id: '__static_export_placeholder__' }];
}

export default function ReportPage() {
  return <ReportClient />;
}
