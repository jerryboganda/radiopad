// A sync that creates the server report but fails the follow-up patch must remember the server id,
// so the retry patches the same report instead of creating a duplicate.
import { describe, it, expect, vi, beforeEach } from 'vitest';

const createMock = vi.fn();
const patchMock = vi.fn();
vi.mock('@/lib/api', () => ({
  api: { reports: { create: (b: unknown) => createMock(b), patch: (id: string, b: unknown) => patchMock(id, b) } },
}));

import { listOfflineDrafts, saveOfflineDraft, syncOfflineDrafts, type OfflineDraft } from '@/lib/offlineDrafts';

const draft: OfflineDraft = {
  localId: 'local-1',
  serverId: null,
  status: 'Draft',
  modality: 'CT',
  bodyPart: 'Chest',
  accessionNumber: 'ACC-1',
  indication: 'cough',
  technique: '',
  comparison: '',
  findings: 'clear',
  impression: 'normal',
  recommendations: '',
  rev: 1,
  updatedAt: 1,
  dirty: true,
};

describe('syncOfflineDrafts', () => {
  beforeEach(async () => {
    createMock.mockReset();
    patchMock.mockReset();
    localStorage.clear();
    await saveOfflineDraft({ ...draft });
  });

  it('does not create a second server report when the patch failed on the first attempt', async () => {
    createMock.mockResolvedValue({ id: 'srv-1' });
    patchMock.mockRejectedValueOnce(new Error('offline again')).mockResolvedValueOnce({});

    expect(await syncOfflineDrafts()).toEqual({ synced: 0, failed: 1 });
    const [afterFail] = await listOfflineDrafts();
    expect(afterFail.serverId).toBe('srv-1');
    expect(afterFail.dirty).toBe(true);

    expect(await syncOfflineDrafts()).toEqual({ synced: 1, failed: 0 });
    expect(createMock).toHaveBeenCalledTimes(1);
    expect(patchMock).toHaveBeenLastCalledWith('srv-1', expect.objectContaining({ findings: 'clear' }));
    const [done] = await listOfflineDrafts();
    expect(done.dirty).toBe(false);
  });
});
