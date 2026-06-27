import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import type { LocalModel } from '@/lib/api';

// The Edge card probes the WebView for Web Speech availability — stub it.
vi.mock('@/lib/dictation/speech', () => ({
  probeWebSpeechAvailable: vi.fn(async () => ({ ok: true })),
}));

// Only `api.localModels.list` runs on mount; stub the whole module.
const listMock = vi.fn();
vi.mock('@/lib/api', () => ({
  api: { localModels: { list: (...a: unknown[]) => listMock(...a) } },
}));

import OnDeviceModels from '@/app/providers/OnDeviceModels';

function model(partial: Partial<LocalModel>): LocalModel {
  return {
    id: 'x',
    displayName: 'X',
    kind: 'Stt',
    engine: 'e',
    sizeBytes: 0,
    license: '',
    placeholder: false,
    downloaded: false,
    available: false,
    isPrimary: false,
    progress: { id: 'x', state: 'NotStarted', bytesDownloaded: 0, totalBytes: 0, error: null },
    ...partial,
  };
}

beforeEach(() => listMock.mockReset());

describe('OnDeviceModels — platform speech engines', () => {
  it('renders the SAPI, Windows language-pack, and Edge cards with their notes + actions', async () => {
    listMock.mockResolvedValue({
      enabled: true,
      models: [
        model({
          id: 'windows-sapi',
          displayName: 'Windows Speech (on-device) — primary speech-to-text',
          engine: 'windows_sapi',
          provisioning: 'WindowsBuiltIn',
          note: 'Built into Windows. Runs fully on-device — audio never leaves the workstation.',
          available: true,
          downloaded: true,
        }),
        model({
          id: 'windows-winrt',
          displayName: 'Windows Speech — languages & accuracy',
          engine: 'windows_sapi',
          provisioning: 'WindowsLanguagePack',
          note: 'Opens Windows speech settings to add languages and improve the on-device Windows recognizer.',
          downloaded: true,
          available: true,
        }),
        model({
          id: 'edge-webspeech',
          displayName: 'Microsoft Edge Speech (online) — speech-to-text',
          engine: 'edge_webspeech',
          provisioning: 'BrowserWebSpeech',
          note: 'Highly accurate, but processed online by Microsoft — audio leaves the device. Avoid for PHI dictation.',
          downloaded: true,
        }),
      ],
    });

    render(<OnDeviceModels />);

    // All three new cards render.
    await waitFor(() => expect(screen.getByText(/Microsoft Edge Speech/)).toBeInTheDocument());
    expect(screen.getByText(/Windows Speech \(on-device\)/)).toBeInTheDocument();
    expect(screen.getByText(/languages & accuracy/)).toBeInTheDocument();

    // The PHI/online note is surfaced on the Edge card.
    expect(screen.getByText(/audio leaves the device/)).toBeInTheDocument();

    // Provisioning-specific actions: SAPI built-in is selectable (no Download button),
    // the language-pack card opens Windows settings, Edge tests in-app.
    expect(screen.getByRole('button', { name: /Open Windows speech settings/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Test in app/ })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Download$/ })).not.toBeInTheDocument();

    // The Edge availability probe resolves and drives the "Available" badge.
    await waitFor(() => expect(screen.getByText('Available')).toBeInTheDocument());
  });
});
