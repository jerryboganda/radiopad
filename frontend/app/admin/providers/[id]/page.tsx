import ProviderOAuthAdminClient from './ProviderOAuthAdminClient';

export const dynamicParams = false;

export function generateStaticParams(): Array<{ id: string }> {
  return [];
}

export default function ProviderOAuthAdminPage() {
  return <ProviderOAuthAdminClient />;
}