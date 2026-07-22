/**
 * Regression test for HANDOFF gotcha #3 — "Generate impression" must draft
 * from what's on screen, not stale/empty server state.
 *
 * Section textareas in ReportClient only persist on blur (`onChange` updates
 * React state; `onBlur` fires the PATCH). The AI job reads the report from the
 * DB, so clicking "Generate impression" straight from typing the Findings —
 * with no blur in between — used to race the save and send EMPTY findings.
 *
 * Phase 6.1 made generation submit-and-continue: `runAi` now `flushEdits()`
 * then `jobs.submit(...)` (the topbar widget owns polling). The invariant this
 * test protects is unchanged — the flush PATCH must land BEFORE the submit, so
 * the job the coordinator picks up drafts from the freshly-typed findings. We
 * mock the JobsProvider/ToastProvider boundaries and record what is persisted
 * to the DB at the instant `submit` is called.
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, fireEvent, screen, waitFor } from '@testing-library/react';
import * as React from 'react';

// Shared mutable state for the fake backend, created in hoisted scope so the
// (hoisted) vi.mock factories below can close over it.
const h = vi.hoisted(() => {
  const initialReport = {
    id: 'r1',
    tenantId: 't1',
    status: 'Draft',
    rulebookId: null,
    templateId: null,
    study: { accessionNumber: 'ACC1', modality: 'CT', bodyPart: 'Chest', indication: 'cough', comparison: '' },
    indication: '',
    technique: '',
    comparison: '',
    findings: '',
    impression: '',
    recommendations: '',
    aiHighlightsJson: '{}',
    updatedAt: '2026-01-01T00:00:00Z',
  };
  const state = {
    db: { ...initialReport } as Record<string, unknown>,
    // Findings persisted to the DB at the moment `jobs.submit` executed.
    submitFindings: null as string | null,
  };
  // The mocked JobsProvider.submit — records the DB findings at submit time.
  const submit = vi.fn(async () => {
    state.submitFindings = String(state.db.findings ?? '');
    return 'job1';
  });
  return { initialReport, state, submit };
});

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), prefetch: vi.fn() }),
}));

// Only the `id` param resolves — the ?aiJob=/?localJob= deep-link is empty here.
vi.mock('@/lib/browserParams', () => ({
  readQueryParam: (name: string) => (name === 'id' ? 'r1' : ''),
}));

// Phase 6.1 — ReportClient submits generation through the global JobsProvider
// and confirms through the toast API; mock both boundaries.
vi.mock('@/components/jobs/JobsProvider', () => ({
  useJobs: () => ({ submit: h.submit, markApplied: vi.fn() }),
  useJobsForReport: () => [],
}));
vi.mock('@/components/ui/ToastProvider', () => ({
  useToast: () => ({ toast: vi.fn(), dismiss: vi.fn() }),
  ToastProvider: ({ children }: { children: React.ReactNode }) => children,
}));

// Sub-components are irrelevant to this flow and pull in extra weight; stub them.
vi.mock('@/app/(desktop)/reports/[id]/RewriteStylePanel', () => ({ default: () => null }));
vi.mock('@/app/(desktop)/reports/[id]/PriorComparePanel', () => ({ default: () => null }));
vi.mock('@/app/(desktop)/reports/[id]/CopyToRisButton', () => ({ default: () => null }));

vi.mock('@/lib/api', () => {
  const api = {
    reports: {
      get: vi.fn(async () => ({ ...h.state.db })),
      patch: vi.fn(async (_id: string, body: Record<string, unknown>) => {
        h.state.db = { ...h.state.db, ...body };
        return { ...h.state.db };
      }),
      signatures: vi.fn(async () => []),
      // RC composer additions — priors lookup (context bar / study panel) and
      // the case-queue list beside the study context.
      prior: vi.fn(async () => ({ current: { id: 'r1', bodyPart: 'Chest' }, prior: null })),
      list: vi.fn(async () => []),
    },
    providers: {
      list: vi.fn(async () => [
        {
          id: 'prov1', name: 'Test Provider', adapter: 'x', model: 'm',
          endpointUrl: '', compliance: 0, enabled: true, priority: 0, apiKeyConfigured: true,
        },
      ]),
    },
    rulebooks: { list: vi.fn(async () => []) },
    templates: { list: vi.fn(async () => []) },
    // Iter-36 — ReportClient fetches the admin catalogs for the study-context dropdowns.
    modalities: { list: vi.fn(async () => []) },
    bodyParts: { list: vi.fn(async () => []) },
    // RBAC mirror: ReportClient calls usePermissions() → api.me() to decide which
    // actions to render. Grant the full reporting permission set.
    me: vi.fn(async () => ({
      user: {
        permissions: ['reports.read', 'reports.draft', 'reports.edit', 'reports.validate', 'reports.sign', 'reports.export'],
        role: 0,
        roleName: 'MedicalDirector',
      },
    })),
  };
  // AiActivityPanel renders the provider PHI-policy label off this map.
  const COMPLIANCE_LABELS: Record<number, string> = {
    0: 'Blocked', 1: 'Sandbox', 2: 'De-identified only', 3: 'PHI-approved', 4: 'Local only',
  };
  return { api, COMPLIANCE_LABELS };
});

import ReportPage from '@/app/(desktop)/reports/[id]/ReportClient';
import { api } from '@/lib/api';

beforeEach(() => {
  h.state.db = { ...h.initialReport };
  h.state.submitFindings = null;
  h.submit.mockClear();
  // This test exercises the flush-before-AI logic, which is identical for both
  // editors; pin the plain-textarea path so it doesn't depend on driving Tiptap.
  window.localStorage.setItem('radiopad:rich-editor', '0');
});

describe('Generate impression — flush before submit (HANDOFF gotcha #3)', () => {
  it('submits with the on-screen findings even when typed right before clicking (no blur)', async () => {
    const { container } = render(<ReportPage />);

    const genBtn = await screen.findByRole('button', { name: 'Generate Impression' });
    // Wait for providers.list() so the button leaves its disabled state.
    await waitFor(() => expect((genBtn as HTMLButtonElement).disabled).toBe(false));

    const findingsTa = container.querySelector(
      '[data-section="findings"] textarea',
    ) as HTMLTextAreaElement;
    expect(findingsTa).not.toBeNull();

    // Type findings but DO NOT blur — only React state changes; no PATCH yet.
    fireEvent.change(findingsTa, { target: { value: 'Liver and spleen are normal.' } });
    expect(h.state.db.findings).toBe(''); // nothing saved to the DB yet

    fireEvent.click(genBtn);

    await waitFor(() => expect(h.submit).toHaveBeenCalledTimes(1));

    // The flush PATCH must have persisted the freshly-typed findings BEFORE submit.
    expect(h.state.submitFindings).toBe('Liver and spleen are normal.');
    const firstPatch = Math.min(...(api.reports.patch as ReturnType<typeof vi.fn>).mock.invocationCallOrder);
    const firstSubmit = h.submit.mock.invocationCallOrder[0];
    expect(firstPatch).toBeLessThan(firstSubmit);

    // The submitted spec is the impression `ai` job for this report.
    expect(h.submit).toHaveBeenCalledWith(
      expect.objectContaining({ origin: 'hosted', kind: 'ai', reportId: 'r1', mode: 'impression' }),
    );
  });
});
