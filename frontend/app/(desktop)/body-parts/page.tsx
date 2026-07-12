'use client';

import { api } from '@/lib/api';
import CatalogManager from '@/components/admin/CatalogManager';

export default function BodyPartsPage() {
  return (
    <CatalogManager
      title="Body Parts"
      subtitle="Manage the anatomical regions available across the reporting module. Modality + body part together select the report template and rulebook (prompts)."
      itemNoun="body part"
      client={api.bodyParts}
      managePermission="body_parts.manage"
    />
  );
}
