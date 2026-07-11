import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';

const routerPushMock = vi.fn();
const meMock = vi.fn();
const listPagedMock = vi.fn();

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: routerPushMock }),
}));

vi.mock('@/lib/api', () => ({
  api: {
    me: () => meMock(),
    reports: { listPaged: (...args: unknown[]) => listPagedMock(...args) },
  },
}));

vi.mock('@/components/ui/AnimatedNumber', () => ({
  default: ({ value }: { value: number }) => <>{value}</>,
}));

import DashboardPage from '@/app/page';

describe('reports navigation', () => {
  beforeEach(() => {
    routerPushMock.mockReset();
    meMock.mockReset().mockResolvedValue({
      tenant: { displayName: 'Test practice' },
      user: { email: 'radiologist@example.test' },
    });
    listPagedMock.mockReset().mockResolvedValue({ items: [], total: 0 });
  });

  it('opens the new-report wizard with client-side routing', async () => {
    render(<DashboardPage />);
    await waitFor(() => expect(listPagedMock).toHaveBeenCalled());

    fireEvent.click(screen.getAllByRole('button', { name: '+ New report' })[0]);

    expect(routerPushMock).toHaveBeenCalledWith('/reports/new');
  });
});
