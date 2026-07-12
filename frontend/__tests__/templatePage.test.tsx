/**
 * Templates page (`/templates`) — tests for template list rendering,
 * section count, create button, and approval status badges.
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, waitFor, screen } from '@testing-library/react';
import * as React from 'react';

const templatesListMock = vi.fn();
const templatesSaveMock = vi.fn();
const templatesApproveMock = vi.fn();
const templatesSubmitMock = vi.fn();
const templatesDeprecateMock = vi.fn();
const templatesPreviewMock = vi.fn();
const templatesUsageMock = vi.fn();

vi.mock('@/lib/api', () => ({
  api: {
    templates: {
      list: () => templatesListMock(),
      save: (...args: unknown[]) => templatesSaveMock(...args),
      approve: (...args: unknown[]) => templatesApproveMock(...args),
      submitForReview: (...args: unknown[]) => templatesSubmitMock(...args),
      deprecate: (...args: unknown[]) => templatesDeprecateMock(...args),
      preview: (...args: unknown[]) => templatesPreviewMock(...args),
      usage: (...args: unknown[]) => templatesUsageMock(...args),
    },
  },
}));

import TemplatesPage from '@/app/(desktop)/templates/page';

const SAMPLE_TEMPLATES = [
  {
    id: 'tpl-1',
    templateId: 'chest_ct_v1',
    name: 'Chest CT',
    modality: 'CT',
    bodyPart: 'Chest',
    subspecialty: 'Thoracic',
    sectionsJson: JSON.stringify({
      sections: [
        { id: 'indication', label: 'Indication' },
        { id: 'findings', label: 'Findings' },
        { id: 'impression', label: 'Impression' },
      ],
    }),
    updatedAt: '2026-01-01T00:00:00Z',
    status: 1, // Approved
    approvedAt: '2026-01-01T00:00:00Z',
  },
  {
    id: 'tpl-2',
    templateId: 'brain_mri_v1',
    name: 'Brain MRI',
    modality: 'MRI',
    bodyPart: 'Head',
    subspecialty: 'Neuroradiology',
    sectionsJson: JSON.stringify({
      sections: [
        { id: 'indication', label: 'Indication' },
        { id: 'technique', label: 'Technique' },
        { id: 'findings', label: 'Findings' },
        { id: 'impression', label: 'Impression' },
        { id: 'recommendations', label: 'Recommendations' },
      ],
    }),
    updatedAt: '2026-02-01T00:00:00Z',
    status: 0, // Draft
    approvedAt: null,
  },
  {
    id: 'tpl-3',
    templateId: 'abdomen_us_v1',
    name: 'Abdomen US',
    modality: 'US',
    bodyPart: 'Abdomen',
    subspecialty: 'Body',
    sectionsJson: JSON.stringify({ sections: [] }),
    updatedAt: '2026-03-01T00:00:00Z',
    status: 2, // Deprecated
    approvedAt: null,
  },
];

describe('templates page', () => {
  beforeEach(() => {
    templatesListMock.mockReset();
  });

  it('template list renders with modality and body part', async () => {
    templatesListMock.mockResolvedValue(SAMPLE_TEMPLATES);
    render(<TemplatesPage />);
    await waitFor(() => {
      expect(screen.getByText('Chest CT')).toBeInTheDocument();
    });
    expect(screen.getByText('Brain MRI')).toBeInTheDocument();
    expect(screen.getByText('Abdomen US')).toBeInTheDocument();
    // Modality columns
    expect(screen.getByText('CT')).toBeInTheDocument();
    expect(screen.getByText('MRI')).toBeInTheDocument();
    expect(screen.getByText('US')).toBeInTheDocument();
    // Body part columns
    expect(screen.getByText('Chest')).toBeInTheDocument();
    expect(screen.getByText('Head')).toBeInTheDocument();
    expect(screen.getByText('Abdomen')).toBeInTheDocument();
  });

  it('template cards show correct template IDs', async () => {
    templatesListMock.mockResolvedValue(SAMPLE_TEMPLATES);
    render(<TemplatesPage />);
    await waitFor(() => {
      expect(screen.getByText('chest_ct_v1')).toBeInTheDocument();
    });
    expect(screen.getByText('brain_mri_v1')).toBeInTheDocument();
    expect(screen.getByText('abdomen_us_v1')).toBeInTheDocument();
  });

  it('"Create" button exists', async () => {
    templatesListMock.mockResolvedValue([]);
    render(<TemplatesPage />);
    await waitFor(() => {
      expect(screen.getByText('+ New template')).toBeInTheDocument();
    });
    const btn = screen.getByText('+ New template') as HTMLButtonElement;
    expect(btn.classList.contains('primary')).toBe(true);
  });

  it('template approval status badges render correctly', async () => {
    templatesListMock.mockResolvedValue(SAMPLE_TEMPLATES);
    const { container } = render(<TemplatesPage />);
    await waitFor(() => {
      expect(screen.getByText('Approved')).toBeInTheDocument();
    });

    // Approved badge should have .ok class
    const approvedBadge = screen.getByText('Approved');
    expect(approvedBadge.classList.contains('badge')).toBe(true);
    expect(approvedBadge.classList.contains('ok')).toBe(true);

    // Draft badge should have .badge class (no extra status class)
    const draftBadge = screen.getByText('Draft');
    expect(draftBadge.classList.contains('badge')).toBe(true);

    // Deprecated badge should have .danger class
    const deprecatedBadge = screen.getByText('Deprecated');
    expect(deprecatedBadge.classList.contains('badge')).toBe(true);
    expect(deprecatedBadge.classList.contains('danger')).toBe(true);
  });
});
