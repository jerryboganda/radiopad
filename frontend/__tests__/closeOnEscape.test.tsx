import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import * as React from 'react';
import { useCloseOnEscape } from '@/lib/useCloseOnEscape';
import SignInRequired from '@/components/ui/SignInRequired';

function Modal({ onClose, active }: { onClose: () => void; active?: boolean }) {
  useCloseOnEscape(onClose, active);
  return <div role="dialog">modal</div>;
}

describe('useCloseOnEscape', () => {
  it('closes on Escape and ignores other keys', () => {
    const onClose = vi.fn();
    render(<Modal onClose={onClose} />);
    fireEvent.keyDown(window, { key: 'Enter' });
    expect(onClose).not.toHaveBeenCalled();
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('does nothing while inactive', () => {
    const onClose = vi.fn();
    render(<Modal onClose={onClose} active={false} />);
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onClose).not.toHaveBeenCalled();
  });
});

describe('SignInRequired', () => {
  it('offers sign-in when signed out', () => {
    render(<SignInRequired />);
    expect(screen.getByText('Sign in required')).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Sign in' }).getAttribute('href')).toMatch(/^\/login\?returnTo=/);
  });

  it('says "No access" and links home when signed in without permission', () => {
    render(<SignInRequired denied surface="You don't have access." />);
    expect(screen.getByText('No access')).toBeTruthy();
    expect(screen.queryByText('Sign in required')).toBeNull();
    expect(screen.getByRole('link', { name: 'Back to dashboard' }).getAttribute('href')).toBe('/');
  });
});
