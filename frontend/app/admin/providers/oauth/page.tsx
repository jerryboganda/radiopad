import ProviderOAuthAdminClient from '../[id]/ProviderOAuthAdminClient';
import PermissionGate from '@/components/ui/PermissionGate';

export default function ProviderOAuthAdminPage() {
  return (
    <PermissionGate permission="providers.manage" title="Provider OAuth">
      <ProviderOAuthAdminClient />
    </PermissionGate>
  );
}