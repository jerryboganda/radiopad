'use client';

import { api } from '@/lib/api';
import CatalogManager from '@/components/admin/CatalogManager';

export default function ModalitiesPage() {
  return (
    <CatalogManager
      title="Modalities"
      subtitle="Manage the imaging modalities available across the reporting module. Modality + body part together select the report template and rulebook (prompts)."
      itemNoun="modality"
      client={api.modalities}
      managePermission="modalities.manage"
    />
  );
}
