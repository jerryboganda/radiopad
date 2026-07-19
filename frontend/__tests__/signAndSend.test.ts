// F8 — "Sign & Send" chains validate → sign → acknowledge → export over the existing gated
// endpoints.
//
// SAFETY: validation runs FIRST, and that ordering is the point. The `sign` endpoint performs NO
// blocker check — only `acknowledge` does — so signing first left a report with unresolved blockers
// PERMANENTLY SIGNED once acknowledge 409'd, with export skipped. The radiologist saw "Sign & Send
// failed" while their Primary signature sat on a blocker-laden report, and could not retry: a
// second `sign` 409s on the existing Primary signature, leaving the report stuck signed,
// unacknowledged and unexportable. A signature is an attestation; it must never be applied on the
// way to a check that can reject.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { api } from '@/lib/api';

const clean = { blockerPresent: false, findings: [], qualityScore: 1 } as never;
const blocked = {
  blockerPresent: true,
  qualityScore: 0.2,
  findings: [
    { ruleId: 'R1', severity: 'Blocker', message: 'Laterality missing' },
    { ruleId: 'R2', severity: 'Warning', message: 'Style nit' },
  ],
} as never;

describe('reports.signAndSend', () => {
  beforeEach(() => vi.restoreAllMocks());

  it('chains validate → sign → acknowledge → export in order', async () => {
    const order: string[] = [];
    vi.spyOn(api.reports, 'validate').mockImplementation(async () => { order.push('validate'); return clean; });
    vi.spyOn(api.reports, 'sign').mockImplementation(async () => { order.push('sign'); return {} as never; });
    vi.spyOn(api.reports, 'acknowledge').mockImplementation(async () => { order.push('ack'); return {} as never; });
    vi.spyOn(api.reports, 'exportText').mockImplementation(async () => { order.push('export'); return 'txt' as never; });

    const res = await api.reports.signAndSend('r1', { format: 'text', note: 'reviewed' });

    expect(order).toEqual(['validate', 'sign', 'ack', 'export']);
    expect(api.reports.sign).toHaveBeenCalledWith('r1', { role: 'Primary', note: 'reviewed' });
    expect(res.format).toBe('text');
  });

  it('does NOT sign when validation blockers remain', async () => {
    vi.spyOn(api.reports, 'validate').mockResolvedValue(blocked);
    const sign = vi.spyOn(api.reports, 'sign').mockResolvedValue({} as never);
    const ack = vi.spyOn(api.reports, 'acknowledge').mockResolvedValue({} as never);
    const exp = vi.spyOn(api.reports, 'exportText').mockResolvedValue('txt' as never);

    await expect(api.reports.signAndSend('r1')).rejects.toThrow();

    // The signature is what must not happen. Everything else follows from that.
    expect(sign).not.toHaveBeenCalled();
    expect(ack).not.toHaveBeenCalled();
    expect(exp).not.toHaveBeenCalled();
  });

  it('says how many blockers must be resolved, counting only Blockers', async () => {
    vi.spyOn(api.reports, 'validate').mockResolvedValue(blocked);

    await expect(api.reports.signAndSend('r1')).rejects.toMatchObject({
      body: { error: expect.stringContaining('1 validation blocker'), kind: 'validation_blocker' },
    });
  });

  it('is RETRYABLE after a partial failure — an existing Primary signature is not fatal', async () => {
    // Simulates a previous attempt that signed and then died at acknowledge or export. Re-running
    // must carry on rather than dead-ending forever on the duplicate-signature conflict.
    vi.spyOn(api.reports, 'validate').mockResolvedValue(clean);
    vi.spyOn(api.reports, 'sign').mockRejectedValue(Object.assign(new Error('conflict'), { status: 409 }));
    const ack = vi.spyOn(api.reports, 'acknowledge').mockResolvedValue({} as never);
    const exp = vi.spyOn(api.reports, 'exportText').mockResolvedValue('txt' as never);

    const res = await api.reports.signAndSend('r1', { format: 'text' });

    expect(ack).toHaveBeenCalled();
    expect(exp).toHaveBeenCalled();
    expect(res.format).toBe('text');
  });

  it('still surfaces a non-conflict sign failure', async () => {
    // Only the "already signed" conflict is tolerated. A permission or server error must never be
    // swallowed into a false success.
    vi.spyOn(api.reports, 'validate').mockResolvedValue(clean);
    vi.spyOn(api.reports, 'sign').mockRejectedValue(Object.assign(new Error('forbidden'), { status: 403 }));
    const ack = vi.spyOn(api.reports, 'acknowledge').mockResolvedValue({} as never);

    await expect(api.reports.signAndSend('r1')).rejects.toThrow('forbidden');
    expect(ack).not.toHaveBeenCalled();
  });

  it('STOPS before export if acknowledge fails', async () => {
    vi.spyOn(api.reports, 'validate').mockResolvedValue(clean);
    vi.spyOn(api.reports, 'sign').mockResolvedValue({} as never);
    vi.spyOn(api.reports, 'acknowledge').mockRejectedValue(new Error('blockers'));
    const exp = vi.spyOn(api.reports, 'exportText').mockResolvedValue('txt' as never);

    await expect(api.reports.signAndSend('r1')).rejects.toThrow('blockers');
    expect(exp).not.toHaveBeenCalled();
  });
});
