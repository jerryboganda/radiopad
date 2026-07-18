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
const reportsCreateMock = vi.fn();
const reportsPatchMock = vi.fn();

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
    reports: {
      create: (...args: unknown[]) => reportsCreateMock(...args),
      patch: (...args: unknown[]) => reportsPatchMock(...args),
    },
  },
}));

const routerPushMock = vi.fn();
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: (...args: unknown[]) => routerPushMock(...args) }),
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
    reportsCreateMock.mockReset();
    reportsPatchMock.mockReset();
    routerPushMock.mockReset();
  });

  it('"Use" starts a report from the template, seeding normal section values (F2)', async () => {
    templatesListMock.mockResolvedValue([
      {
        id: 'tpl-9',
        templateId: 'ct_head_normal',
        name: 'CT Head (normal)',
        modality: 'CT',
        bodyPart: 'Head',
        subspecialty: 'Neuro',
        sectionsJson: JSON.stringify({
          sections: [
            { id: 'technique', label: 'Technique', normal: 'Axial CT of the head without contrast.' },
            { id: 'findings', label: 'Findings', normal: 'No acute intracranial abnormality.' },
            { id: 'impression', label: 'Impression', normal: '' }, // empty → not seeded
          ],
        }),
        updatedAt: '2026-01-01T00:00:00Z',
        status: 1,
      },
    ]);
    reportsCreateMock.mockResolvedValue({ id: 'rep-1' });
    reportsPatchMock.mockResolvedValue({ id: 'rep-1' });

    const { fireEvent } = await import('@testing-library/react');
    render(<TemplatesPage />);
    await waitFor(() => expect(screen.getByText('CT Head (normal)')).toBeInTheDocument());

    fireEvent.click(screen.getByRole('button', { name: /^Use$/ }));

    await waitFor(() =>
      expect(reportsCreateMock).toHaveBeenCalledWith({ modality: 'CT', bodyPart: 'Head', templateId: 'tpl-9' }),
    );
    expect(reportsPatchMock).toHaveBeenCalledWith('rep-1', {
      technique: 'Axial CT of the head without contrast.',
      findings: 'No acute intracranial abnormality.',
    });
    await waitFor(() => expect(routerPushMock).toHaveBeenCalledWith('/reports/rep-1'));
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
