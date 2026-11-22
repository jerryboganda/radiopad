/**
 * CatalogManager — a failed list fetch must render ErrorState (with retry), never an empty list.
 */
import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import * as React from 'react';

vi.mock('@/lib/permissions', () => ({
  usePermissions: () => ({ loading: false, can: () => true }),
}));

import CatalogManager from '@/components/admin/CatalogManager';

const list = vi.fn();
// Module-level so the `client` prop identity is stable across renders (reload depends on it).
const client = { list: () => list(), save: vi.fn(), remove: vi.fn() };

describe('CatalogManager load failure', () => {
  it('shows an error with retry instead of an empty list, then recovers', async () => {
    list.mockRejectedValueOnce(new Error('API 500')).mockResolvedValueOnce([{ id: '1', code: 'CT', name: 'Computed Tomography' }]);
    render(
      <CatalogManager title="Modalities" subtitle="x" itemNoun="modality" client={client} managePermission="modalities.manage" />,
    );

    expect(await screen.findByRole('alert')).toHaveTextContent('API 500');
    expect(screen.queryByText('No modality entries yet')).toBeNull();

    fireEvent.click(screen.getByRole('button', { name: /try again/i }));
    expect(await screen.findByText('Computed Tomography')).toBeInTheDocument();
  });
});
