/**
 * Iter-36 — Model evaluation harness test.
 *
 *  1. Eval form renders + posts the right sandbox-compare payload.
 *  2. Per-provider results table renders after submit.
 *  3. Promote button is hidden for non-MedicalDirectors and visible for
 *     MedicalDirectors.
 *  4. Forbidden banner for a Radiologist.
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import * as React from 'react';

const meMock = vi.fn();
const providersListMock = vi.fn();
const rulebooksListMock = vi.fn();
const rulebooksApproveMock = vi.fn();
const validationPacksListMock = vi.fn();
const validationPacksRunMock = vi.fn();
const reportsListPagedMock = vi.fn();
const sandboxCompareMock = vi.fn();

vi.mock('@/lib/api', () => ({
  api: {
    me: () => meMock(),
    providers: { list: () => providersListMock() },
    rulebooks: {
      list: () => rulebooksListMock(),
      approve: (id: string) => rulebooksApproveMock(id),
    },
    validationPacks: {
      list: () => validationPacksListMock(),
      run: (id: string) => validationPacksRunMock(id),
    },
    reports: { listPaged: (p: unknown) => reportsListPagedMock(p) },
    ai: { sandboxCompare: (b: unknown) => sandboxCompareMock(b) },
  },
  COMPLIANCE_LABELS: {
    0: 'Blocked',
    1: 'Sandbox',
    2: 'De-identified only',
    3: 'PHI-approved',
    4: 'Local only',
  },
}));

import AdminModelEvalPage from '@/app/admin/model-eval/page';

// The page gates UI off the backend-authoritative permission set returned by
// `api.me()` (`user.permissions`), via `can(...)` from @/lib/permissions:
//   - dashboard visibility  → 'audit.verify'
//   - promote-to-production → 'rulebooks.approve'
// Mirror the real RolePermissionMap so each role exercises the right branch:
//   role 2 = MedicalDirector    → audit.verify + rulebooks.approve (full)
//   role 3 = ComplianceReviewer → audit.verify, NO rulebooks.approve (read-only)
//   role 0 = Radiologist        → neither (forbidden banner)
function permissionsForRole(role: number): string[] {
  switch (role) {
    case 2: // MedicalDirector — oversight + can promote rulebooks
      return ['audit.verify', 'audit.read', 'rulebooks.read', 'rulebooks.approve', 'validation_packs.read', 'validation_packs.run'];
    case 3: // ComplianceReviewer — oversight only, cannot promote
      return ['audit.verify', 'audit.read', 'rulebooks.read', 'validation_packs.read', 'validation_packs.run'];
    default: // Radiologist (0) and others — no oversight access
      return ['reports.read', 'reports.draft', 'reports.edit'];
  }
}

function seed(role: number) {
  meMock.mockResolvedValue({
    tenant: { slug: 'it', displayName: 'IT' },
    user: { email: 'u@radiopad.local', role, permissions: permissionsForRole(role) },
  });
  providersListMock.mockResolvedValue([
    {
      id: 'openai-sandbox',
      name: 'OpenAI Sandbox',
      adapter: 'openai',
      model: 'gpt-4o',
      endpointUrl: 'https://api.openai.com/v1',
      compliance: 1, // Sandbox
      enabled: true,
      priority: 0,
      apiKeyConfigured: true,
    },
    {
      id: 'phi-prod',
      name: 'Prod PHI',
      adapter: 'azure',
      model: 'gpt-4',
      endpointUrl: 'https://prod',
      compliance: 3, // PHI-approved — must NOT appear in selector
      enabled: true,
      priority: 0,
      apiKeyConfigured: true,
    },
  ]);
  rulebooksListMock.mockResolvedValue([
    {
      id: 'rb-1',
      rulebookId: 'chest_ct_v1',
      name: 'Chest CT',
      version: '1.0.0',
      owner: 'thoracic',
      status: 0,
      appliesToModalities: 'CT',
      appliesToBodyParts: 'Chest',
    },
  ]);
  validationPacksListMock.mockResolvedValue([
    {
      id: 'pack-1',
      rulebookId: 'chest_ct_v1',
      version: '1.0.0',
      name: 'Chest CT golden',
      status: 'Approved',
      approvedAt: null,
      approvedBy: null,
      createdAt: '2026-01-01T00:00:00Z',
      createdBy: 'u',
      caseCount: 6,
    },
  ]);
  reportsListPagedMock.mockResolvedValue({
    items: [
      {
        id: 'rep-1',
        tenantId: 't',
        status: 1,
        rulebookId: 'chest_ct_v1',
        templateId: null,
        study: {
          accessionNumber: 'A1',
          modality: 'CT',
          bodyPart: 'Chest',
          indication: '',
          comparison: '',
        },
        indication: '',
        technique: '',
        comparison: '',
        findings: '',
        impression: '',
        recommendations: '',
        aiHighlightsJson: '[]',
        updatedAt: '2026-01-01',
      },
    ],
    total: 1,
  });
  sandboxCompareMock.mockResolvedValue({
    runs: [
      {
        providerId: 'openai-sandbox',
        provider: 'OpenAI Sandbox',
        model: 'gpt-4o',
        output: 'Hello world',
        latencyMs: 250,
        inputTokens: 50,
        outputTokens: 11,
        error: null,
      },
    ],
  });
  validationPacksRunMock.mockResolvedValue({
    passed: 5,
    failed: 1,
    totalCases: 6,
    failures: [],
  });
}

describe('admin/model-eval', () => {
  beforeEach(() => {
    meMock.mockReset();
    providersListMock.mockReset();
    rulebooksListMock.mockReset();
    rulebooksApproveMock.mockReset();
    validationPacksListMock.mockReset();
    validationPacksRunMock.mockReset();
    reportsListPagedMock.mockReset();
    sandboxCompareMock.mockReset();
  });

  it('renders form, posts sandbox-compare payload, shows results', async () => {
    seed(2); // MedicalDirector
    render(<AdminModelEvalPage />);
    await waitFor(() => {
      expect(screen.getByTestId('panel-eval-form')).toBeInTheDocument();
    });

    // Fill the form.
    fireEvent.change(screen.getByTestId('select-rulebook'), {
      target: { value: 'chest_ct_v1' },
    });
    fireEvent.change(screen.getByTestId('select-pack'), {
      target: { value: 'pack-1' },
    });
    fireEvent.change(screen.getByTestId('select-report'), {
      target: { value: 'rep-1' },
    });
    fireEvent.click(screen.getByTestId('provider-openai-sandbox'));

    // Only sandbox providers should be selectable.
    expect(screen.queryByTestId('provider-phi-prod')).toBeNull();

    fireEvent.click(screen.getByTestId('run-eval'));

    await waitFor(() => {
      expect(sandboxCompareMock).toHaveBeenCalledTimes(1);
    });
    expect(sandboxCompareMock).toHaveBeenCalledWith({
      reportId: 'rep-1',
      mode: 'impression',
      providerIds: ['openai-sandbox'],
    });
    expect(validationPacksRunMock).toHaveBeenCalledWith('pack-1');

    // Results table renders.
    await waitFor(() => {
      expect(screen.getByTestId('panel-eval-results')).toBeInTheDocument();
    });
    expect(screen.getByText('250 ms')).toBeInTheDocument();
  });

  it('shows the promote button only for Medical Director', async () => {
    seed(2);
    render(<AdminModelEvalPage />);
    await waitFor(() => {
      expect(screen.getByTestId('promote-btn')).toBeInTheDocument();
    });
    expect(screen.queryByTestId('promote-locked')).toBeNull();
  });

  it('hides the promote button for Compliance Reviewer', async () => {
    seed(3);
    render(<AdminModelEvalPage />);
    await waitFor(() => {
      expect(screen.getByTestId('promote-locked')).toBeInTheDocument();
    });
    expect(screen.queryByTestId('promote-btn')).toBeNull();
  });

  it('shows forbidden banner for Radiologist', async () => {
    seed(0);
    render(<AdminModelEvalPage />);
    await waitFor(() => {
      expect(screen.getByTestId('model-eval-forbidden')).toBeInTheDocument();
    });
    expect(screen.queryByTestId('panel-eval-form')).toBeNull();
  });
});
