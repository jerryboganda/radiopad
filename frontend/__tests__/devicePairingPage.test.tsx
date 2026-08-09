import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, act } from '@testing-library/react';
import React from 'react';
import DevicePairingPage from '../app/(desktop)/device-pairing/page';
import { CompanionProvider } from '../components/companion/CompanionContext';
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
      transcribe: vi.fn().mockResolvedValue({ transcript: 'test' }),
    },
  },
  companionBase: () => 'https://admin.radiopadstudio.com',
  companionWsBase: () => 'wss://admin.radiopadstudio.com',
  getActiveAuthToken: () => 'mock-token',
}));

vi.mock('../lib/companion', () => ({
  connectCompanion: vi.fn().mockReturnValue({
    sendDictation: vi.fn(),
    sendCommand: vi.fn(),
    sendSectionContext: vi.fn(),
    sendSignal: vi.fn(),
    send: vi.fn(),
    close: vi.fn(),
    state: () => 'open',
  }),
}));

describe('DevicePairingPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders the header, idle pairing card, and test sandbox', () => {
    render(
      <CompanionProvider>
        <DevicePairingPage />
      </CompanionProvider>
    );

    expect(screen.getByRole('heading', { name: /Device pairing/i, level: 1 })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Start pairing/i })).toBeInTheDocument();
    expect(screen.getByText(/Pre-reporting test sandbox/i)).toBeInTheDocument();
  });

  it('transitions to advertising state showing QR and pairing code on clicking Start pairing', async () => {
    (api.companion.createSession as any).mockResolvedValue({
      sessionId: 'sess-abc',
      pairingCode: 'RAD123',
      companionToken: 'token-xyz',
      tenantSlug: 'clinic',
      userEmail: 'doctor@example.com',
    });

    render(
      <CompanionProvider>
        <DevicePairingPage />
      </CompanionProvider>
    );

    const startBtn = screen.getByRole('button', { name: /Start pairing/i });
    await act(async () => {
      fireEvent.click(startBtn);
    });

    expect(await screen.findByText(/RAD123/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Cancel/i })).toBeInTheDocument();
  });
});
