import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, waitFor } from '@testing-library/react';

const routerReplaceMock = vi.fn();

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn(), replace: routerReplaceMock }),
}));

import Home from '@/app/page';

describe('desktop home', () => {
  beforeEach(() => {
    routerReplaceMock.mockReset();
  });

  it('redirects the root route to the Dashboard (RC IA) with client-side routing', async () => {
    const { container } = render(<Home />);
    // Shows a busy skeleton while the client-side redirect runs.
    expect(container.querySelector('[aria-busy="true"]')).not.toBeNull();
    await waitFor(() => expect(routerReplaceMock).toHaveBeenCalledWith('/dashboard'));
  });
});
