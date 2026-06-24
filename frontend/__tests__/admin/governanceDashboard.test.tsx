/**
 * Iter-36 — Governance dashboard test.
 *
 * Renders the actual `/admin/governance` page with mocked api + role and
 * asserts:
 *  1. All six panels render (data-testid="panel-*").
 *  2. Compliance Reviewer sees the page (read-only).
 *  3. Radiologist is shown the forbidden banner.
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import * as React from 'react';

// Mock the typed API client. Each test seeds it before render.
const meMock = vi.fn();
const providersListMock = vi.fn();
const providersHealthMock = vi.fn();
const rulebooksListMock = vi.fn();
const promptOverridesListMock = vi.fn();
const usageSummaryMock = vi.fn();
const analyticsSummaryMock = vi.fn();
const auditQueryMock = vi.fn();

vi.mock('@/lib/api', () => ({
  api: {
    me: () => meMock(),
    providers: {
      list: () => providersListMock(),
      health: (id: string) => providersHealthMock(id),
    },
    rulebooks: { list: () => rulebooksListMock() },
    promptOverrides: { list: () => promptOverridesListMock() },
    usage: { summary: () => usageSummaryMock() },
    analytics: { summary: () => analyticsSummaryMock() },
    audit: { query: (p: unknown) => auditQueryMock(p) },
  },
  COMPLIANCE_LABELS: {
    0: 'Blocked',
    1: 'Sandbox',
    2: 'De-identified only',
    3: 'PHI-approved',
    4: 'Local only',
  },
}));

// Avoid pulling in the real next/link runtime (which expects an App
// Router context); render an anchor passthrough.
vi.mock('next/link', () => ({
  default: ({ href, children, ...rest }: React.PropsWithChildren<{ href: string }>) => (
    <a href={href} {...rest}>{children}</a>
  ),
}));

import AdminGovernancePage from '@/app/admin/governance/page';

const SAMPLE_USAGE = {
  totalRequests: 42,
  inputTokens: 100,
  outputTokens: 200,
  avgLatencyMs: 1234,
  costTotalUsd: 0.5678,
  byProvider: [
    {
      provider: 'openai',
      adapter: 'openai',
      requests: 42,
      inputTokens: 100,
      outputTokens: 200,
      avgLatencyMs: 1234,
      costTotalUsd: 0.5678,
      unpriced: false,
    },
  ],
};

const SAMPLE_ANALYTICS = {
  reports: { total: 1, validated: 1, exported: 0, validationPassRate: 1 },
  ai: { totalRequests: 42, totalCostUsd: 0.5678 },
  governance: { phiViolationsBlocked: 3 },
};

// The governance page gates on `can(me.user.permissions, 'audit.verify')`
// (via lib/permissions.ts → usePermissions → api.me). The mocked api.me()
// must therefore return a `permissions` array matching the role under test:
// the oversight roles (Medical Director / Compliance Reviewer / IT Admin)
// carry `audit.verify` and see the panels; the Radiologist does not and is
// shown the forbidden banner.
const PERMISSIONS_BY_ROLE: Record<number, string[]> = {
  // Radiologist (0): drafting only — NO audit.verify → forbidden banner.
  0: ['reports.read', 'reports.draft', 'reports.edit'],
  // Medical Director (2): full oversight + reporting authority.
  2: [
    'reports.read', 'reports.draft', 'reports.edit', 'reports.validate', 'reports.sign', 'reports.export',
    'rulebooks.read', 'rulebooks.manage', 'rulebooks.approve',
    'templates.read', 'templates.manage', 'templates.approve',
    'providers.read', 'audit.read', 'audit.verify', 'audit.export',
    'validation_packs.read', 'validation_packs.run',
    'prompt_overrides.manage', 'prompt_overrides.approve',
  ],
  // Compliance Reviewer (3): read-only oversight — has audit.verify but no mutation perms.
  3: [
    'reports.read', 'rulebooks.read', 'templates.read', 'providers.read',
    'audit.read', 'audit.verify', 'audit.export', 'validation_packs.read',
  ],
};

const ROLE_NAMES: Record<number, string> = {
  0: 'Radiologist',
  2: 'MedicalDirector',
  3: 'ComplianceReviewer',
};

function seedHappyPath(role = 2 /* MedicalDirector */) {
  meMock.mockResolvedValue({
    tenant: { slug: 'it', displayName: 'IT' },
    user: {
      email: 'md@radiopad.local',
      role,
      roleName: ROLE_NAMES[role],
      permissions: PERMISSIONS_BY_ROLE[role] ?? [],
    },
  });
  providersListMock.mockResolvedValue([
    {
      id: 'openai',
      name: 'OpenAI',
      adapter: 'openai',
      model: 'gpt-4',
      endpointUrl: 'https://api.openai.com/v1',
      compliance: 3,
      enabled: true,
      priority: 0,
      apiKeyConfigured: true,
      retentionLabel: 'baa-30d',
    },
  ]);
  rulebooksListMock.mockResolvedValue([
    {
      id: 'rb-1',
      rulebookId: 'chest_ct_v1',
      name: 'Chest CT',
      version: '1.0.0',
      owner: 'thoracic',
      status: 2,
      appliesToModalities: 'CT',
      appliesToBodyParts: 'Chest',
      updatedAt: '2026-01-01T00:00:00Z',
    },
  ]);
  promptOverridesListMock.mockResolvedValue([]);
  usageSummaryMock.mockResolvedValue(SAMPLE_USAGE);
  analyticsSummaryMock.mockResolvedValue(SAMPLE_ANALYTICS);
  auditQueryMock.mockResolvedValue([
    {
      id: 'a1',
      action: 44,
      createdAt: '2026-06-01T00:00:00Z',
      detailsJson: JSON.stringify({ passed: 5, failed: 1, packId: 'pack-1' }),
    },
    {
      id: 'a2',
      action: 40,
      createdAt: '2026-06-02T00:00:00Z',
      detailsJson: JSON.stringify({ kind: 'cost-spike', message: 'Spike detected' }),
    },
    {
      id: 'a3',
      action: 5,
      createdAt: '2026-06-03T00:00:00Z',
      detailsJson: JSON.stringify({ reason: 'phi-blocked' }),
    },
  ]);
}

describe('admin/governance', () => {
  beforeEach(() => {
    meMock.mockReset();
    providersListMock.mockReset();
    providersHealthMock.mockReset();
    rulebooksListMock.mockReset();
    promptOverridesListMock.mockReset();
    usageSummaryMock.mockReset();
    analyticsSummaryMock.mockReset();
    auditQueryMock.mockReset();
  });

  it('renders all six governance panels for a Medical Director', async () => {
    seedHappyPath(2);
    render(<AdminGovernancePage />);
    await waitFor(() => {
      expect(screen.getByTestId('panel-model-inventory')).toBeInTheDocument();
    });
    for (const id of [
      'panel-model-inventory',
      'panel-versions',
      'panel-ai-usage',
      'panel-phi-routing',
      'panel-validation-results',
      'panel-drift-alerts',
    ]) {
      expect(screen.getByTestId(id)).toBeInTheDocument();
    }
    // Validation totals: 5 passed / 1 failed.
    expect(screen.getByText('5 passed')).toBeInTheDocument();
    expect(screen.getByText('1 failed')).toBeInTheDocument();
    // PHI block count from analytics.
    expect(screen.getByText('3')).toBeInTheDocument();
  });

  it('renders read-only for Compliance Reviewer (no provider mutation UI)', async () => {
    seedHappyPath(3); // ComplianceReviewer
    render(<AdminGovernancePage />);
    await waitFor(() => {
      expect(screen.getByTestId('panel-model-inventory')).toBeInTheDocument();
    });
    // Page is read-only; the only interactive control is the on-demand
    // health probe button. There are no save/delete buttons on this page.
    const buttons = Array.from(document.querySelectorAll('button'));
    const labels = buttons.map((b) => (b.textContent || '').trim().toLowerCase());
    for (const forbidden of ['save', 'delete', 'remove', 'edit']) {
      expect(labels).not.toContain(forbidden);
    }
  });

  it('shows the forbidden banner for a Radiologist', async () => {
    seedHappyPath(0); // Radiologist
    render(<AdminGovernancePage />);
    await waitFor(() => {
      expect(screen.getByTestId('governance-forbidden')).toBeInTheDocument();
    });
    expect(screen.queryByTestId('panel-model-inventory')).toBeNull();
  });
});
