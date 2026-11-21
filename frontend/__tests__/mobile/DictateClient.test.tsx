import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import * as React from 'react';

const push = vi.fn();
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push }),
  useSearchParams: () => new URLSearchParams(''),
}));
const getReportById = vi.fn();
vi.mock('@/lib/api/reportingClient', () => ({ getReportById: (...a: unknown[]) => getReportById(...a) }));
vi.mock('@/app/(mobile)/reporting/components/AudioRecorderControls', () => ({ default: () => null }));

import DictateClient from '@/app/(mobile)/reporting/dictate/DictateClient';

describe('DictateClient', () => {
  it('shows "No report selected" instead of an endless skeleton when the id is missing', () => {
    render(<DictateClient />);
    expect(screen.getByText('No report selected')).toBeTruthy();
    expect(document.querySelector('[aria-busy="true"]')).toBeNull();
    expect(getReportById).not.toHaveBeenCalled();
    screen.getByRole('button', { name: 'Go to reports' }).click();
    expect(push).toHaveBeenCalledWith('/reporting');
  });
});
