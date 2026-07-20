// Per-user default AI engine preference — the radiologist's own choice of
// provider (cloud, UBAG, or local model). Verifies persistence and the
// defaulting rules every AI surface (report editor, new-report wizard,
// AI Assistant hub) resolves through.
import { describe, expect, it, beforeEach } from 'vitest';
import {
  getPreferredProviderId,
  setPreferredProviderId,
  resolveDefaultProvider,
} from '@/lib/ai/providerPref';
import type { Provider } from '@/lib/api';

function provider(id: string, enabled = true, priority = 999): Provider {
  return {
    id,
    name: id,
    adapter: 'test',
    model: 'm',
    endpointUrl: '',
    compliance: 3,
    enabled,
    priority,
    apiKeyConfigured: true,
  };
}

describe('preferred provider (per-user AI engine choice)', () => {
  beforeEach(() => setPreferredProviderId(''));

  it('persists and round-trips the saved engine', () => {
    setPreferredProviderId('medgemma');
    expect(getPreferredProviderId()).toBe('medgemma');
    expect(window.localStorage.getItem('radiopad:preferred-provider')).toBe('medgemma');
  });

  it('clearing removes the stored key', () => {
    setPreferredProviderId('x');
    setPreferredProviderId('');
    expect(getPreferredProviderId()).toBe('');
    expect(window.localStorage.getItem('radiopad:preferred-provider')).toBeNull();
  });

  it('resolveDefaultProvider prefers the saved engine when enabled', () => {
    setPreferredProviderId('local');
    const rows = [provider('cloud', true, 1), provider('local', true, 5)];
    expect(resolveDefaultProvider(rows)?.id).toBe('local');
  });

  it('ignores a saved engine that is disabled or gone, falling back to priority', () => {
    setPreferredProviderId('gone');
    const rows = [provider('b', true, 2), provider('a', true, 1), provider('off', false, 0)];
    expect(resolveDefaultProvider(rows)?.id).toBe('a');

    setPreferredProviderId('off');
    expect(resolveDefaultProvider(rows)?.id).toBe('a');
  });

  it('falls back to the first row when nothing is enabled', () => {
    const rows = [provider('only', false)];
    expect(resolveDefaultProvider(rows)?.id).toBe('only');
    expect(resolveDefaultProvider([])).toBeUndefined();
  });
});
