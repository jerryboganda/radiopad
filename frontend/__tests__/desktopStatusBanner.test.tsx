import { describe, it, expect } from 'vitest';
import { render, act } from '@testing-library/react';
import * as React from 'react';
import DesktopStatusBanner from '../components/DesktopStatusBanner';

describe('DesktopStatusBanner', () => {
  it('stays hidden for the ready desktop backend state', () => {
    const { container } = render(<DesktopStatusBanner />);
    act(() => {
      window.dispatchEvent(new CustomEvent('radiopad:backend-status', {
        detail: { state: 'ready' },
      }));
    });
    expect(container.querySelector('.rp-desktop-status')).toBeNull();
  });

  it('renders degraded backend state with locked banner classes', () => {
    const { container, getByRole } = render(<DesktopStatusBanner />);
    act(() => {
      window.dispatchEvent(new CustomEvent('radiopad:backend-status', {
        detail: {
          state: 'degraded',
          message: 'backend readiness endpoint is not ready',
          restartCount: 1,
        },
      }));
    });
    const banner = getByRole('status');
    expect(banner.className).toContain('banner warn rp-desktop-status');
    expect(banner.textContent).toContain('Local RadioPad service is not ready');
    expect(container.querySelector('.rp-desktop-status-meta')?.textContent)
      .toContain('Restart attempt 1');
  });

  it('renders failed backend state as a danger banner', () => {
    const { getByRole } = render(<DesktopStatusBanner />);
    act(() => {
      window.dispatchEvent(new CustomEvent('radiopad:backend-status', {
        detail: { state: 'failed', message: 'backend sidecar unavailable' },
      }));
    });
    const banner = getByRole('status');
    expect(banner.className).toContain('banner danger rp-desktop-status');
    expect(banner.textContent).toContain('backend sidecar unavailable');
  });
});
