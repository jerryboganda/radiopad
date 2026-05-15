/**
 * Marketplace page (`/marketplace`) — tests for tab rendering,
 * listing cards, install button, and submit form.
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, fireEvent, waitFor, screen } from '@testing-library/react';
import * as React from 'react';

const listMock = vi.fn();
const listSubmissionsMock = vi.fn();
const checkoutMock = vi.fn();
const installMock = vi.fn();
const submitForReviewMock = vi.fn();
const approveSubmissionMock = vi.fn();
const rejectSubmissionMock = vi.fn();

vi.mock('@/lib/api', () => ({
  api: {
    marketplace: {
      list: () => listMock(),
      listSubmissions: () => listSubmissionsMock(),
      checkout: (...args: unknown[]) => checkoutMock(...args),
      install: (...args: unknown[]) => installMock(...args),
      submitForReview: (...args: unknown[]) => submitForReviewMock(...args),
      approveSubmission: (...args: unknown[]) => approveSubmissionMock(...args),
      rejectSubmission: (...args: unknown[]) => rejectSubmissionMock(...args),
    },
  },
}));

import MarketplacePage from '@/app/marketplace/page';

const SAMPLE_LISTINGS = [
  {
    id: 'ls-1',
    name: 'Chest CT Rulebook',
    description: 'A comprehensive chest CT rulebook',
    kind: 'rulebook',
    priceCents: 0,
    reviewedAt: '2026-01-01',
    installCount: 42,
  },
  {
    id: 'ls-2',
    name: 'Neuro MRI Pack',
    description: 'Prompt pack for neuro MRI',
    kind: 'prompt_pack',
    priceCents: 999,
    reviewedAt: '2026-02-01',
    installCount: 7,
  },
];

describe('marketplace page', () => {
  beforeEach(() => {
    listMock.mockReset();
    listSubmissionsMock.mockReset();
    installMock.mockReset();
    submitForReviewMock.mockReset();
    listMock.mockResolvedValue(SAMPLE_LISTINGS);
    listSubmissionsMock.mockResolvedValue([]);
  });

  it('Browse/My Submissions/Review tabs render', async () => {
    render(<MarketplacePage />);
    await waitFor(() => {
      expect(screen.getByText('Browse')).toBeInTheDocument();
    });
    expect(screen.getByText('My Submissions')).toBeInTheDocument();
    // Review tab shows pending count
    expect(screen.getByText('Review (0)')).toBeInTheDocument();
  });

  it('listing cards show install count badges', async () => {
    render(<MarketplacePage />);
    await waitFor(() => {
      expect(screen.getByText('Chest CT Rulebook')).toBeInTheDocument();
    });
    expect(screen.getByText('42 installs')).toBeInTheDocument();
    expect(screen.getByText('7 installs')).toBeInTheDocument();
  });

  it('Install button exists on listings', async () => {
    render(<MarketplacePage />);
    await waitFor(() => {
      expect(screen.getByText('Chest CT Rulebook')).toBeInTheDocument();
    });
    const installButtons = screen.getAllByText('Install');
    expect(installButtons.length).toBe(2);
    for (const btn of installButtons) {
      expect((btn as HTMLButtonElement).classList.contains('primary')).toBe(true);
    }
  });

  it('submit form shows category selector', async () => {
    render(<MarketplacePage />);
    await waitFor(() => {
      expect(screen.getByText('Submit to Marketplace')).toBeInTheDocument();
    });
    // Open the submit form
    fireEvent.click(screen.getByText('Submit to Marketplace'));
    await waitFor(() => {
      expect(screen.getByTestId('submit-form')).toBeInTheDocument();
    });
    // Category selector should be present
    expect(screen.getByText('Category')).toBeInTheDocument();
    expect(screen.getByText('Rulebook')).toBeInTheDocument();
    expect(screen.getByText('Template')).toBeInTheDocument();
    expect(screen.getByText('Prompt Pack')).toBeInTheDocument();
  });
});
