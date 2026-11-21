import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import * as React from 'react';

vi.mock('next/link', () => ({
  default: ({ href, children, ...rest }: { href: string; children: React.ReactNode }) => (
    <a href={href} {...rest}>{children}</a>
  ),
}));

import GettingStarted from '@/components/ui/GettingStarted';

const steps = [{ href: '/settings/models', title: 'Choose your AI model', detail: 'pick one' }];

describe('GettingStarted', () => {
  beforeEach(() => localStorage.clear());

  it('lists the steps as links and stays dismissed on the next visit', () => {
    const first = render(<GettingStarted id="t" title="Get set up" steps={steps} />);
    expect(screen.getByText('Choose your AI model').getAttribute('href')).toBe('/settings/models');
    fireEvent.click(screen.getByText('Dismiss'));
    expect(screen.queryByText('Choose your AI model')).toBeNull();
    first.unmount();

    render(<GettingStarted id="t" title="Get set up" steps={steps} />);
    expect(screen.queryByText('Choose your AI model')).toBeNull();
  });
});
