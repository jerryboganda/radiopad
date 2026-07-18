// F8 — "Sign & Send" chains sign → acknowledge → export over the existing gated endpoints.
// SAFETY: it must STOP before export if sign or acknowledge fails, so the sign-off gate is never
// bypassed (nothing is exported for a report that couldn't be signed/acknowledged).
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { api } from '@/lib/api';

describe('reports.signAndSend', () => {
  beforeEach(() => vi.restoreAllMocks());

  it('chains sign → acknowledge → export in order', async () => {
    const order: string[] = [];
    vi.spyOn(api.reports, 'sign').mockImplementation(async () => { order.push('sign'); return {} as never; });
    vi.spyOn(api.reports, 'acknowledge').mockImplementation(async () => { order.push('ack'); return {} as never; });
    vi.spyOn(api.reports, 'exportText').mockImplementation(async () => { order.push('export'); return 'txt' as never; });

    const res = await api.reports.signAndSend('r1', { format: 'text', note: 'reviewed' });

    expect(order).toEqual(['sign', 'ack', 'export']);
    expect(api.reports.sign).toHaveBeenCalledWith('r1', { role: 'Primary', note: 'reviewed' });
    expect(res.format).toBe('text');
  });

  it('STOPS before acknowledge/export if sign fails (gate not bypassed)', async () => {
    vi.spyOn(api.reports, 'sign').mockRejectedValue(new Error('validation blockers'));
    const ack = vi.spyOn(api.reports, 'acknowledge').mockResolvedValue({} as never);
    const exp = vi.spyOn(api.reports, 'exportText').mockResolvedValue('txt' as never);

    await expect(api.reports.signAndSend('r1')).rejects.toThrow('validation blockers');
    expect(ack).not.toHaveBeenCalled();
    expect(exp).not.toHaveBeenCalled();
  });

  it('STOPS before export if acknowledge fails', async () => {
    vi.spyOn(api.reports, 'sign').mockResolvedValue({} as never);
    vi.spyOn(api.reports, 'acknowledge').mockRejectedValue(new Error('blockers'));
    const exp = vi.spyOn(api.reports, 'exportText').mockResolvedValue('txt' as never);

    await expect(api.reports.signAndSend('r1')).rejects.toThrow('blockers');
    expect(exp).not.toHaveBeenCalled();
  });
});
