/**
 * Regression test for HANDOFF gotcha #3 — "Generate impression" must draft
 * from what's on screen, not stale/empty server state.
 *
 * Section textareas in ReportClient only persist on blur (`onChange` updates
 * React state; `onBlur` fires the PATCH). The AI endpoint
 * (`POST /api/reports/{id}/ai`) reads the report from the DB, so clicking
 * "Generate impression" straight from typing the Findings — with no blur in
 * between — used to race the save and send EMPTY findings to the model
 * ("No findings were provided in the report…").
 *
 * This test mounts the real ReportClient with a stateful fake API where the
 * AI mock records whatever `findings` are persisted at the moment it runs.
 * The fix flushes the editor state with a synchronous PATCH before the AI
 * call, so the recorded findings must equal the freshly-typed text.
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, fireEvent, screen, waitFor } from '@testing-library/react';
import * as React from 'react';

// Shared mutable state for the fake backend, created in hoisted scope so the
// (hoisted) vi.mock factory below can close over it.
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
  return {
    initialReport,
    state: {
      db: { ...initialReport } as Record<string, unknown>,
      // Findings the AI saw in the DB at the moment runAi executed.
      aiReadFindings: null as string | null,
    },
  };
});

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), prefetch: vi.fn() }),
}));

vi.mock('@/lib/browserParams', () => ({
  readQueryParam: () => 'r1',
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
      runAi: vi.fn(async (_id: string, body: { mode: string }) => {
        // The real backend reads findings from the DB — mirror that here.
        h.state.aiReadFindings = String(h.state.db.findings ?? '');
        return {
          text: `IMPRESSION drafted from: ${h.state.db.findings}`,
          provider: 'p',
          model: 'm',
          latencyMs: 1,
          promptVersion: 'v1',
          mode: body.mode,
        };
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
    // RBAC mirror (added in the per-controller RBAC pass): ReportClient calls
    // usePermissions() → api.me() to decide which actions to render. Grant the
    // full reporting permission set so editor affordances (Generate impression,
    // etc.) are shown.
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
  h.state.aiReadFindings = null;
  // This test exercises the flush-before-AI logic, which is identical for both
  // editors; pin the plain-textarea path so it doesn't depend on driving Tiptap.
  window.localStorage.setItem('radiopad:rich-editor', '0');
});

describe('Generate impression — flush before AI (HANDOFF gotcha #3)', () => {
  it('sends the on-screen findings to the AI even when typed right before clicking (no blur)', async () => {
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

    await waitFor(() => expect(h.state.aiReadFindings).not.toBeNull());

    // The AI must have seen the freshly-typed findings, not the empty DB value.
    expect(h.state.aiReadFindings).toBe('Liver and spleen are normal.');
    // And the flush PATCH must have run before the AI call.
    const firstPatch = Math.min(...(api.reports.patch as ReturnType<typeof vi.fn>).mock.invocationCallOrder);
    const firstAi = (api.reports.runAi as ReturnType<typeof vi.fn>).mock.invocationCallOrder[0];
    expect(firstPatch).toBeLessThan(firstAi);
  });
});
