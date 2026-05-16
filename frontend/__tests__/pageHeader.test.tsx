import { describe, it, expect } from 'vitest';
import { render } from '@testing-library/react';
import * as React from 'react';
import PageHeader from '@/components/shell/PageHeader';

describe('PageHeader', () => {
  it('renders title and description', () => {
    const { getByRole, getByText } = render(
      <PageHeader title="Reports" description="Tenant — signed in as user@example.com" />,
    );
    expect(getByRole('heading', { name: 'Reports', level: 1 })).toBeInTheDocument();
    expect(getByText('Tenant — signed in as user@example.com')).toBeInTheDocument();
  });

  it('renders the primary action in the action slot', () => {
    const { getByRole } = render(
      <PageHeader title="Reports" primaryAction={<button className="primary">+ New report</button>} />,
    );
    expect(getByRole('button', { name: '+ New report' })).toBeInTheDocument();
  });

  it('omits the action area when no actions provided', () => {
    const { container } = render(<PageHeader title="Reports" />);
    expect(container.querySelector('.rp-page-actions')).toBeNull();
  });

  it('uses the canonical class names', () => {
    const { container } = render(<PageHeader title="Reports" description="desc" primaryAction={<button>x</button>} />);
    expect(container.querySelector('header.rp-page-header')).not.toBeNull();
    expect(container.querySelector('h1.rp-page-title')).not.toBeNull();
    expect(container.querySelector('p.rp-page-sub')).not.toBeNull();
    expect(container.querySelector('.rp-page-actions')).not.toBeNull();
  });
});
