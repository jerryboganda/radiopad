import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import React from 'react';
import { CompanionProvider, useCompanion } from '../components/companion/CompanionContext';
import { api } from '../lib/api';

vi.mock('../lib/api', () => ({
  api: {
    companion: {
      createSession: vi.fn(),
      pair: vi.fn(),
      endSession: vi.fn().mockResolvedValue(undefined),
      getSession: vi.fn(),
    },
    localModels: {
      list: vi.fn().mockResolvedValue({ enabled: false, models: [] }),
    },
    reports: {
      transcribe: vi.fn().mockResolvedValue({ transcript: 'normal chest x-ray' }),
    },
  },
  companionBase: () => 'https://admin.radiopadstudio.com',
  companionWsBase: () => 'wss://admin.radiopadstudio.com',
  getActiveAuthToken: () => 'mock-token',
}));

describe('CompanionContext', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('provides initial idle state', () => {
    const wrapper = ({ children }: { children: React.ReactNode }) => (
      <CompanionProvider>{children}</CompanionProvider>
    );
    const { result } = renderHook(() => useCompanion(), { wrapper });

    expect(result.current.phase).toBe('idle');
    expect(result.current.link).toBe('idle');
    expect(result.current.sessionId).toBeNull();
    expect(result.current.pairingCode).toBeNull();
    expect(result.current.companionDeviceName).toBeNull();
    expect(result.current.phoneListening).toBe(false);
    expect(result.current.transcribing).toBe(false);
  });

  it('transitions to advertising state on startPairing', async () => {
    (api.companion.createSession as any).mockResolvedValue({
      sessionId: 'sess-123',
      pairingCode: 'CODE99',
      companionToken: 'comp-tok-123',
      tenantSlug: 'test-tenant',
      userEmail: 'rad@example.com',
    });

    const wrapper = ({ children }: { children: React.ReactNode }) => (
      <CompanionProvider>{children}</CompanionProvider>
    );
    const { result } = renderHook(() => useCompanion(), { wrapper });

    await act(async () => {
      await result.current.startPairing();
    });

    expect(result.current.phase).toBe('advertising');
    expect(result.current.sessionId).toBe('sess-123');
    expect(result.current.pairingCode).toBe('CODE99');
    expect(result.current.qrDataUrl).toBeTruthy();
  });

  it('unpair resets state to idle and calls endSession', async () => {
    (api.companion.createSession as any).mockResolvedValue({
      sessionId: 'sess-123',
      pairingCode: 'CODE99',
      companionToken: 'comp-tok-123',
      tenantSlug: 'test-tenant',
      userEmail: 'rad@example.com',
    });

    const wrapper = ({ children }: { children: React.ReactNode }) => (
      <CompanionProvider>{children}</CompanionProvider>
    );
    const { result } = renderHook(() => useCompanion(), { wrapper });

    await act(async () => {
      await result.current.startPairing();
    });

    await act(async () => {
      await result.current.unpair();
    });

    expect(result.current.phase).toBe('idle');
    expect(result.current.sessionId).toBeNull();
    expect(result.current.pairingCode).toBeNull();
    expect(api.companion.endSession).toHaveBeenCalledWith('sess-123');
  });
});
