import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, waitFor, screen, fireEvent } from '@testing-library/react';
import * as React from 'react';

const providersListMock = vi.fn();
const providersSaveMock = vi.fn();
const providersHealthMock = vi.fn();
const reportsListMock = vi.fn();
const sandboxCompareMock = vi.fn();
const ubagStatusMock = vi.fn();

vi.mock('@/lib/api', () => ({
  COMPLIANCE_LABELS: {
    0: 'Blocked',
    1: 'No patient data',
    2: 'De-identified only',
    3: 'Safe for patient data',
    4: 'Runs on-site',
  },
  api: {
    providers: {
      list: () => providersListMock(),
      save: (...args: unknown[]) => providersSaveMock(...args),
      health: (...args: unknown[]) => providersHealthMock(...args),
    },
    reports: {
      list: () => reportsListMock(),
    },
    ai: {
      sandboxCompare: (...args: unknown[]) => sandboxCompareMock(...args),
    },
    ubag: {
      status: () => ubagStatusMock(),
    },
  },
}));

import ProvidersPage from '@/app/providers/page';

describe('providers page', () => {
  beforeEach(() => {
    providersListMock.mockReset();
    providersSaveMock.mockReset();
    providersHealthMock.mockReset();
    reportsListMock.mockReset();
    sandboxCompareMock.mockReset();
    ubagStatusMock.mockReset();
    providersListMock.mockResolvedValue([]);
    providersSaveMock.mockResolvedValue({ id: 'provider-1' });
    reportsListMock.mockResolvedValue([]);
    ubagStatusMock.mockResolvedValue({
      health: { ok: true, status: 'ok' },
      browser: { instances: 1, contexts: 1, tabs: 1 },
      targets: [],
      allowedTargets: ['gemini_web', 'deepseek_web', 'mock'],
      orderedTargets: ['gemini_web', 'deepseek_web', 'mock'],
    });
  });

  it('offers Copilot, Gemini, UBAG, and OpenAI-compatible presets', async () => {
    render(<ProvidersPage />);

    await waitFor(() => expect(screen.getByText('Available models')).toBeInTheDocument());
    fireEvent.click(screen.getByText('+ Add a model'));

    const preset = screen.getByLabelText('Preset') as HTMLSelectElement;
    const presetLabels = Array.from(preset.options).map((option) => option.textContent);

    expect(presetLabels).toContain('GitHub Copilot SDK');
    expect(presetLabels).toContain('GitHub Copilot CLI');
    expect(presetLabels).toContain('Gemini CLI');
    expect(presetLabels).toContain('UBAG automation hub');
    expect(presetLabels).toContain('OpenAI-compatible');
  });

  const sandboxPresetCases: Array<{ preset: string; name: string; adapter: string; model: string }> = [
    { preset: 'github-copilot-sdk', name: 'Copilot SDK', adapter: 'github-copilot-sdk', model: 'copilot' },
    { preset: 'github-copilot-cli', name: 'Copilot CLI', adapter: 'github-copilot-cli', model: 'copilot' },
    { preset: 'gemini-cli', name: 'Gemini CLI', adapter: 'gemini-cli', model: '' },
    { preset: 'codex-cli', name: 'Codex CLI', adapter: 'codex-cli', model: '' },
    { preset: 'openai-compatible', name: 'OpenAI-compatible', adapter: 'openai-compatible', model: '' },
    { preset: 'ubag', name: 'UBAG', adapter: 'ubag', model: 'gemini_web' },
  ];

  it.each(sandboxPresetCases)('applies sandbox defaults for $name and saves through the API client', async ({ preset, name, adapter, model }) => {
    render(<ProvidersPage />);

    await waitFor(() => expect(screen.getByText('Available models')).toBeInTheDocument());
    fireEvent.click(screen.getByText('+ Add a model'));

    fireEvent.change(screen.getByLabelText('Preset'), { target: { value: preset } });
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: name } });

    expect((screen.getByLabelText('Adapter') as HTMLSelectElement).value).toBe(adapter);
    expect((screen.getByLabelText('Compliance class') as HTMLSelectElement).value).toBe('1');
    // For ubag, Model is a <select>; for other adapters it is a text <input>.
    // Both expose .value so we cast to the union type.
    expect((screen.getByLabelText('Model') as HTMLInputElement | HTMLSelectElement).value).toBe(model);

    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(providersSaveMock).toHaveBeenCalled());
    expect(providersSaveMock.mock.calls[0][0]).toMatchObject({
      name,
      adapter,
      compliance: 1,
      model,
    });
  });

  it('lists backend-canonical adapter ids in the adapter picker', async () => {
    render(<ProvidersPage />);

    await waitFor(() => expect(screen.getByText('Available models')).toBeInTheDocument());
    fireEvent.click(screen.getByText('+ Add a model'));

    const adapter = screen.getByLabelText('Adapter') as HTMLSelectElement;
    const values = Array.from(adapter.options).map((option) => option.value);

    expect(values).toContain('openai');
    expect(values).toContain('google-vertex');
    expect(values).toContain('openai-compatible');
    expect(values).toContain('github-copilot-sdk');
    expect(values).toContain('github-copilot-cli');
    expect(values).toContain('gemini-cli');
    expect(values).toContain('ubag');
    expect(values).not.toContain('openai-direct');
    expect(values).not.toContain('google-vertex-ai');
  });

  it('renders an empty state with a retryable add action', async () => {
    render(<ProvidersPage />);

    await waitFor(() => expect(screen.getByText('No AI models yet')).toBeInTheDocument());
    fireEvent.click(screen.getByText('Add model'));

    expect(screen.getByText('New provider')).toBeInTheDocument();
  });

  it('renders a retryable error state when providers fail to load', async () => {
    providersListMock.mockRejectedValueOnce(new Error('backend offline')).mockResolvedValueOnce([]);

    render(<ProvidersPage />);

    await waitFor(() => expect(screen.getByText("Couldn't load models")).toBeInTheDocument());
    expect(screen.getByText('backend offline')).toBeInTheDocument();

    fireEvent.click(screen.getByText('Try again'));
    await waitFor(() => expect(screen.getByText('No AI models yet')).toBeInTheDocument());
    expect(providersListMock).toHaveBeenCalledTimes(2);
  });

  it('renders a select of UBAG targets when adapter is ubag', async () => {
    render(<ProvidersPage />);

    await waitFor(() => expect(screen.getByText('Available models')).toBeInTheDocument());
    fireEvent.click(screen.getByText('+ Add a model'));

    // Switch to ubag adapter
    fireEvent.change(screen.getByLabelText('Adapter'), { target: { value: 'ubag' } });

    // Wait for the ubag status fetch + re-render
    await waitFor(() => {
      const modelEl = screen.getByLabelText('Model');
      expect(modelEl.tagName).toBe('SELECT');
    });

    const modelSelect = screen.getByLabelText('Model') as HTMLSelectElement;
    const optionValues = Array.from(modelSelect.options).map((o) => o.value);
    expect(optionValues).toContain('gemini_web');
    expect(optionValues).toContain('deepseek_web');
    expect(optionValues).toContain('mock');
  });

  it('renders a text input for Model when adapter is not ubag', async () => {
    render(<ProvidersPage />);

    await waitFor(() => expect(screen.getByText('Available models')).toBeInTheDocument());
    fireEvent.click(screen.getByText('+ Add a model'));

    // Default adapter is 'mock' — Model should be a plain text input
    const modelEl = screen.getByLabelText('Model');
    expect(modelEl.tagName).toBe('INPUT');

    // Switch to anthropic — still a text input
    fireEvent.change(screen.getByLabelText('Adapter'), { target: { value: 'anthropic' } });
    expect(screen.getByLabelText('Model').tagName).toBe('INPUT');
  });

  it('seeds model to fallback first entry when ubag status fetch rejects (e.g. 403)', async () => {
    ubagStatusMock.mockRejectedValueOnce(new Error('403'));

    render(<ProvidersPage />);

    await waitFor(() => expect(screen.getByText('Available models')).toBeInTheDocument());
    fireEvent.click(screen.getByText('+ Add a model'));

    // Switch adapter to ubag without applying a preset (draft.model starts as '')
    fireEvent.change(screen.getByLabelText('Adapter'), { target: { value: 'ubag' } });

    // Even though status() rejected, the synchronous seed should have fired
    await waitFor(() => {
      const modelEl = screen.getByLabelText('Model');
      expect(modelEl.tagName).toBe('SELECT');
    });

    expect((screen.getByLabelText('Model') as HTMLSelectElement).value).toBe('gemini_web');
  });

  it('shows provider health status returned by the API', async () => {
    providersListMock.mockResolvedValueOnce([
      {
        id: 'p1',
        name: 'Compat',
        adapter: 'openai-compatible',
        model: 'llama',
        endpointUrl: '',
        compliance: 1,
        enabled: true,
        priority: 10,
        apiKeyConfigured: false,
        quality: 0.5,
        retentionLabel: '',
      },
    ]);
    providersHealthMock.mockResolvedValueOnce({
      ok: false,
      error: 'endpoint_private_network_blocked',
      note: null,
      status: null,
      runtime: 'openai-compatible',
      probedAt: '2026-05-19T00:00:00Z',
    });

    render(<ProvidersPage />);

    await waitFor(() => expect(screen.getAllByText('Compat').length).toBeGreaterThan(0));
    fireEvent.click(screen.getByText('Test'));

    await waitFor(() => expect(screen.getByText(/Unavailable: endpoint_private_network_blocked/)).toBeInTheDocument());
  });
});
