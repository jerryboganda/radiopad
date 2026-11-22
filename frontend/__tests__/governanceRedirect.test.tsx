/** Legacy `/governance` forwards to `/admin/governance/` so old links keep working. */
import { describe, it, expect, vi } from 'vitest';
import { render } from '@testing-library/react';

const replace = vi.fn();
vi.mock('next/navigation', () => ({ useRouter: () => ({ replace }) }));

import GovernanceRedirect from '@/app/(web)/governance/page';

describe('/governance redirect', () => {
  it('replaces the route with /admin/governance/', () => {
    render(<GovernanceRedirect />);
    expect(replace).toHaveBeenCalledWith('/admin/governance/');
  });
});
