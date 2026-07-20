import { describe, it, expect, vi, beforeEach } from 'vitest';
import type { Provider } from '@/lib/api';

const listMock = vi.fn();
const saveMock = vi.fn();

vi.mock('@/lib/api', () => ({
  api: {
    providers: {
      list: (...a: unknown[]) => listMock(...a),
      save: (...a: unknown[]) => saveMock(...a),
    },
  },
}));

import {
  ensureOnDeviceProvider,
  findOnDeviceProvider,
  LOCAL_LLAMA_ADAPTER,
  LOCAL_LLAMA_ENDPOINT,
} from '@/lib/models/onDeviceProvider';

const MODEL_ID = 'medgemma-1.5-4b-q4';

function provider(partial: Partial<Provider>): Provider {
  return {
    id: 'p1',
    name: 'MedGemma (on-device)',
    adapter: LOCAL_LLAMA_ADAPTER,
    model: MODEL_ID,
    endpointUrl: LOCAL_LLAMA_ENDPOINT,
    compliance: 4,
    enabled: true,
    priority: 100,
    apiKeyConfigured: false,
    ...partial,
  };
}

beforeEach(() => {
  listMock.mockReset();
  saveMock.mockReset();
});

describe('findOnDeviceProvider', () => {
  it('matches on adapter AND model, not adapter alone', () => {
    const rows = [
      provider({ id: 'other', model: 'some-other-gguf' }),
      provider({ id: 'mine', model: MODEL_ID }),
      provider({ id: 'cloud', adapter: 'openai', model: MODEL_ID }),
    ];
    expect(findOnDeviceProvider(rows, MODEL_ID)?.id).toBe('mine');
  });

  it('returns undefined when nothing is registered', () => {
    expect(findOnDeviceProvider([], MODEL_ID)).toBeUndefined();
  });
});

describe('ensureOnDeviceProvider', () => {
  /**
   * The load-bearing test. `SaveProviderDto` gives Compliance, Enabled and Priority no
   * defaults, so an omitted field deserializes to 0 — meaning Blocked, disabled, and
   * *top* priority. `resolveDefaultProvider` picks the lowest priority number as the
   * tenant default, so omitting `priority` would quietly make one radiologist's
   * on-device model the default provider for every colleague in the tenant.
   */
  it('sends compliance, enabled and priority explicitly when creating', async () => {
    listMock.mockResolvedValueOnce([]).mockResolvedValueOnce([provider({})]);
    saveMock.mockResolvedValue({ id: 'p1' });

    const res = await ensureOnDeviceProvider(MODEL_ID, 'MedGemma');

    expect(res.status).toBe('created');
    expect(saveMock).toHaveBeenCalledTimes(1);
    const body = saveMock.mock.calls[0][0] as Record<string, unknown>;
    expect(body.adapter).toBe(LOCAL_LLAMA_ADAPTER);
    expect(body.model).toBe(MODEL_ID);
    expect(body.endpointUrl).toBe(LOCAL_LLAMA_ENDPOINT);
    expect(body.compliance).toBe(4); // LocalOnly — 0 would be Blocked
    expect(body.enabled).toBe(true);
    expect(body.priority).toBe(100); // neutral — 0 would make it the tenant default
    expect(body.id).toBeNull();
  });

  it('binds the endpoint to loopback so PHI cannot leave the device', async () => {
    listMock.mockResolvedValueOnce([]).mockResolvedValueOnce([provider({})]);
    saveMock.mockResolvedValue({ id: 'p1' });

    await ensureOnDeviceProvider(MODEL_ID, 'MedGemma');

    const body = saveMock.mock.calls[0][0] as { endpointUrl: string };
    expect(new URL(body.endpointUrl).hostname).toBe('127.0.0.1');
  });

  it('is a no-op when an enabled row already exists', async () => {
    listMock.mockResolvedValueOnce([provider({ enabled: true })]);

    const res = await ensureOnDeviceProvider(MODEL_ID, 'MedGemma');

    expect(res.status).toBe('already');
    expect(saveMock).not.toHaveBeenCalled();
  });

  it('re-enables a disabled row in place, keeping its id and operator-set priority', async () => {
    const existing = provider({ id: 'existing', enabled: false, priority: 5, name: 'Renamed' });
    listMock
      .mockResolvedValueOnce([existing])
      .mockResolvedValueOnce([{ ...existing, enabled: true }]);
    saveMock.mockResolvedValue({ id: 'existing' });

    const res = await ensureOnDeviceProvider(MODEL_ID, 'MedGemma');

    expect(res.status).toBe('enabled');
    const body = saveMock.mock.calls[0][0] as Record<string, unknown>;
    expect(body.id).toBe('existing');
    expect(body.priority).toBe(5); // an admin's deliberate ranking is not overwritten
    expect(body.name).toBe('Renamed'); // nor their rename
    expect(body.enabled).toBe(true);
  });

  it('reports forbidden rather than throwing when the user lacks ProvidersManage', async () => {
    listMock.mockResolvedValueOnce([]);
    saveMock.mockRejectedValue(Object.assign(new Error('Forbidden'), { status: 403 }));

    await expect(ensureOnDeviceProvider(MODEL_ID, 'MedGemma')).resolves.toEqual({
      status: 'forbidden',
    });
  });

  it('propagates non-403 failures instead of silently reporting success', async () => {
    listMock.mockResolvedValueOnce([]);
    saveMock.mockRejectedValue(Object.assign(new Error('boom'), { status: 500 }));

    await expect(ensureOnDeviceProvider(MODEL_ID, 'MedGemma')).rejects.toThrow('boom');
  });
});
