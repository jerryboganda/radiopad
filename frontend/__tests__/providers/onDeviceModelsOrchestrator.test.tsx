import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import type { LocalModel } from '@/lib/api';

vi.mock('@/lib/dictation/speech', () => ({
  probeWebSpeechAvailable: vi.fn(async () => ({ ok: true })),
}));

const listMock = vi.fn();
const providersListMock = vi.fn();
vi.mock('@/lib/api', () => ({
  api: {
    localModels: { list: (...a: unknown[]) => listMock(...a) },
    providers: { list: (...a: unknown[]) => providersListMock(...a) },
  },
}));

import OnDeviceModels from '@/components/models/OnDeviceModels';

const LOCAL_MODEL = 'on-device-1.5-4b-q4';

function localModel(partial: Partial<LocalModel> = {}): LocalModel {
  return {
    id: LOCAL_MODEL,
    displayName: 'On-device 1.5 4B (Q4_K_M) — on-device report formatter',
    kind: 'Orchestrator',
    engine: 'llama-cpp',
    sizeBytes: 2489894976,
    license: 'HAI-DEF / Gemma',
    placeholder: false,
    provisioning: 'HostedFile',
    downloaded: true,
    available: true,
    supportsPrimary: false,
    isPrimary: false,
    runtime: { id: 'llama-server-b10068', installed: true, running: true },
    progress: { id: LOCAL_MODEL, state: 'Ready', bytesDownloaded: 0, totalBytes: 0, error: null },
    ...partial,
  };
}

function renderWith(model: LocalModel, providers: unknown[] = []) {
  listMock.mockResolvedValue({ enabled: true, models: [model] });
  providersListMock.mockResolvedValue(providers);
  render(<OnDeviceModels />);
}

beforeEach(() => {
  listMock.mockReset();
  providersListMock.mockReset();
});

describe('OnDeviceModels — orchestrator card', () => {
  /**
   * The original defect: the card offered "Make primary", which the backend rejects
   * for any non-STT model with a 400, and "Test", which searched the STT engine list
   * and returned "model not downloaded" for a model sitting on disk.
   */
  it('never offers "Make primary" for an orchestrator model', async () => {
    renderWith(localModel());
    await waitFor(() => expect(screen.getByText(/On-device/)).toBeInTheDocument());
    expect(screen.queryByRole('button', { name: /Make primary/ })).not.toBeInTheDocument();
  });

  it('offers "Use for report generation" once the chain is complete', async () => {
    renderWith(localModel());
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /Use for report generation/ })).toBeEnabled(),
    );
  });

  it('shows "Setup incomplete" — not "Ready" — when the runtime is missing', async () => {
    renderWith(
      localModel({
        available: false,
        runtime: { id: 'llama-server-b10068', installed: false, running: false },
      }),
    );
    await waitFor(() => expect(screen.getByText('Setup incomplete')).toBeInTheDocument());
    expect(screen.queryByText('Ready')).not.toBeInTheDocument();
  });

  it('renders each prerequisite link so the missing one is identifiable', async () => {
    renderWith(
      localModel({
        available: false,
        runtime: { id: 'llama-server-b10068', installed: false, running: false },
      }),
    );
    await waitFor(() => expect(screen.getByText('Model file')).toBeInTheDocument());
    expect(screen.getByText('llama.cpp runtime')).toBeInTheDocument();
    expect(screen.getByText('Local server')).toBeInTheDocument();
  });

  it('marks the card as already registered when a provider row exists', async () => {
    renderWith(localModel(), [
      { id: 'p1', name: 'On-device', adapter: 'llama-cpp', model: LOCAL_MODEL, enabled: true },
    ]);
    await waitFor(() => expect(screen.getByText('In report generation')).toBeInTheDocument());
    expect(screen.queryByRole('button', { name: /Use for report generation/ })).not.toBeInTheDocument();
  });

  it('surfaces the download size before the model is fetched', async () => {
    renderWith(localModel({ downloaded: false, available: false, runtime: null }));
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /Download 2\.32 GB/ })).toBeInTheDocument(),
    );
    expect(screen.getByText('Not downloaded')).toBeInTheDocument();
  });

  /** The repair path — a corrupt model otherwise reports "Ready" forever. */
  it('always offers re-download once the model is installed', async () => {
    renderWith(localModel());
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /Re-download/ })).toBeInTheDocument(),
    );
  });

  it('reports real download progress rather than a fabricated percentage', async () => {
    renderWith(
      localModel({
        downloaded: false,
        available: false,
        progress: {
          id: LOCAL_MODEL,
          state: 'Downloading',
          bytesDownloaded: 1244947488,
          totalBytes: 2489894976,
          error: null,
        },
      }),
    );
    const bar = await screen.findByRole('progressbar');
    expect(bar).toHaveAttribute('aria-valuenow', '50');
    expect(screen.getByText('50%')).toBeInTheDocument();
  });

  it('omits aria-valuenow while verifying, when no honest percentage exists', async () => {
    renderWith(
      localModel({
        downloaded: false,
        available: false,
        progress: {
          id: LOCAL_MODEL,
          state: 'Verifying',
          bytesDownloaded: 2489894976,
          totalBytes: 2489894976,
          error: null,
        },
      }),
    );
    const bar = await screen.findByRole('progressbar');
    expect(bar).not.toHaveAttribute('aria-valuenow');
    expect(bar).toHaveAttribute('data-indeterminate', 'true');
  });
});
