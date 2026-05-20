/**
 * Analytics dashboard (`/analytics`) — tests for period preset buttons,
 * Product KPIs, Governance KPIs, and KPI color coding.
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, waitFor, screen } from '@testing-library/react';
import * as React from 'react';

const analyticsSummaryMock = vi.fn();

vi.mock('@/lib/api', () => ({
  api: {
    analytics: {
      summary: (...args: unknown[]) => analyticsSummaryMock(...args),
    },
  },
}));

import AnalyticsPage from '@/app/analytics/page';

const SAMPLE_SUMMARY = {
  window: { from: '2026-01-01', to: '2026-01-31' },
  product: {
    draftAcceptanceRate: 0.85,
    impressionAcceptanceRate: 0.9,
    timeSavedPerReport: 120,
    validationPassRate: 0.95,
    contradictionDetectionRate: 1.5,
    editDistance: 0.15,
    activeRadiologists: 12,
    rulebookAdoption: 0.88,
    providerCostPerReport: 0.0234,
    turnaroundTimeImpact: 45,
    avgQualityScore: 82,
  },
  governance: {
    unapprovedPromptUsage: 0,
    phiViolationsBlocked: 3,
    rulebookRegressionFailures: 0,
    modelDriftAlerts: 1,
    auditCompleteness: 0.98,
  },
  ai: {
    totalRequests: 500,
    okCount: 480,
    blockedCount: 10,
    errorCount: 10,
    inputTokens: 50000,
    outputTokens: 25000,
    avgLatencyMs: 450,
    costTotalUsd: 12.34,
    byProvider: [
      {
        provider: 'openai',
        adapter: 'openai',
        requests: 500,
        inputTokens: 50000,
        outputTokens: 25000,
        costInputUsd: 6.0,
        costOutputUsd: 6.34,
        costTotalUsd: 12.34,
        unpriced: false,
      },
    ],
  },
};

describe('analytics page', () => {
  beforeEach(() => {
    analyticsSummaryMock.mockReset();
  });

  it('period preset buttons render (7d, 30d, 90d)', async () => {
    analyticsSummaryMock.mockResolvedValue(SAMPLE_SUMMARY);
    render(<AnalyticsPage />);
    await waitFor(() => {
      expect(screen.getByText('7 days')).toBeInTheDocument();
    });
    expect(screen.getByText('30 days')).toBeInTheDocument();
    expect(screen.getByText('90 days')).toBeInTheDocument();
    expect(screen.getByText('Custom')).toBeInTheDocument();
  });

  it('productivity and quality section renders all 11 tiles', async () => {
    analyticsSummaryMock.mockResolvedValue(SAMPLE_SUMMARY);
    render(<AnalyticsPage />);
    await waitFor(() => {
      expect(screen.getByText('Productivity & quality')).toBeInTheDocument();
    });
    const kpiLabels = [
      'Draft acceptance rate',
      'Impression acceptance rate',
      'Time saved / report',
      'Validation pass rate',
      'Contradiction rate / 100',
      'Edit distance',
      'Active radiologists',
      'Rulebook adoption',
      'Provider cost / report',
      'TAT impact (median)',
      'Avg quality score',
    ];
    for (const label of kpiLabels) {
      expect(screen.getByText(label)).toBeInTheDocument();
    }
  });

  it('Governance KPIs section renders all 5 tiles', async () => {
    analyticsSummaryMock.mockResolvedValue(SAMPLE_SUMMARY);
    render(<AnalyticsPage />);
    await waitFor(() => {
      expect(screen.getByText('Governance KPIs (§18.2)')).toBeInTheDocument();
    });
    const govLabels = [
      'Unapproved prompt usage',
      'PHI violations blocked',
      'Rulebook regression failures',
      'Model drift alerts',
      'Audit completeness',
    ];
    for (const label of govLabels) {
      expect(screen.getByText(label)).toBeInTheDocument();
    }
  });

  it('KPI values show correct color coding (ok/warn for thresholds)', async () => {
    analyticsSummaryMock.mockResolvedValue(SAMPLE_SUMMARY);
    const { container } = render(<AnalyticsPage />);
    await waitFor(() => {
      expect(screen.getByText('Productivity & quality')).toBeInTheDocument();
    });

    // draftAcceptanceRate = 0.85 >= 0.8 → ok
    const okBadges = container.querySelectorAll('.badge.ok');
    expect(okBadges.length).toBeGreaterThan(0);

    // modelDriftAlerts = 1 > 0 → warn
    const warnBadges = container.querySelectorAll('.badge.warn');
    expect(warnBadges.length).toBeGreaterThan(0);

    // unapprovedPromptUsage = 0 → ok badge
    // phiViolationsBlocked = 3 → info badge (not zero, but info severity)
    const infoBadges = container.querySelectorAll('.badge.info');
    expect(infoBadges.length).toBeGreaterThan(0);
  });
});
