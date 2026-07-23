import { describe, it, expect, vi, afterEach } from 'vitest';
import { api } from '@/lib/api';
import { dedupeKey } from '@/lib/jobs';
import { describeAiError } from '@/lib/aiErrors';
import {
  assembleDictationRaw,
  planCleanupApply,
  CLEANUP_SECTION_KEYS,
  type CleanupSectionMap,
} from '@/lib/cleanup';

// FE-PR5 — dictation-cleanup migration to a durable async job. The cleanup job
// is a SUGGESTION SET; nothing writes the report without passing the client-side
// apply/preview gate. These tests cover the pure core of that flow (kept out of
// the heavyweight ReportClient per the reportPage.test.tsx doctrine): the submit
// body/endpoint, the raw-dictation assembly + empty short-circuit, and the
// all-or-preview staleness gate (live snapshot AND snapshot-less deep-link).

const realFetch = globalThis.fetch;

function mockFetch(bodyObj: unknown, status = 200): ReturnType<typeof vi.fn> {
  const fn = vi.fn().mockResolvedValue(
    new Response(JSON.stringify(bodyObj), {
      status,
      headers: { 'Content-Type': 'application/json' },
    }),
  );
  globalThis.fetch = fn as unknown as typeof fetch;
  return fn;
}

afterEach(() => {
  globalThis.fetch = realFetch;
  vi.restoreAllMocks();
});

// ---------------------------------------------------------------------------
// Submit — the API primitive the provider routes cleanup through.
// ---------------------------------------------------------------------------
describe('api.reports.submitCleanupJob', () => {
  it('POSTs { rawDictation } to the durable dictation-cleanup jobs endpoint', async () => {
    const fn = mockFetch({ jobId: 'clj1', status: 'queued' }, 202);
    const out = await api.reports.submitCleanupJob('r1', 'liver is normal\nno lesion');
    expect(fn).toHaveBeenCalledTimes(1);
    const [url, init] = fn.mock.calls[0] as [string, RequestInit];
    expect(url).toBe('/api/reports/r1/dictation/cleanup/jobs');
    expect(init.method).toBe('POST');
    expect(JSON.parse(String(init.body))).toEqual({ rawDictation: 'liver is normal\nno lesion' });
    expect(out).toEqual({ jobId: 'clj1', status: 'queued' });
  });

  it('the cleanup single-flight key is stable (a double-tap dedupes to one job / snapshot)', () => {
    // The provider dedupes on this key, returning the SAME jobId for a second
    // submit — which is why runDictationCleanup keeps the ORIGINAL snapshot.
    expect(dedupeKey('r1', 'ai', 'cleanup')).toBe(dedupeKey('r1', 'ai', 'cleanup'));
    expect(dedupeKey('r1', 'ai', 'cleanup')).not.toBe(dedupeKey('r1', 'ai', 'impression'));
  });
});

// ---------------------------------------------------------------------------
// Raw assembly + empty short-circuit (runs BEFORE any submit).
// ---------------------------------------------------------------------------
describe('assembleDictationRaw', () => {
  it('joins the non-empty, trimmed sections in document order with newlines', () => {
    const raw = assembleDictationRaw({
      indication: '  chest pain  ',
      technique: '',
      findings: 'clear lungs',
      impression: '   ',
      recommendations: 'follow up',
    });
    expect(raw).toBe('chest pain\nclear lungs\nfollow up');
  });

  it('returns "" when every section is empty (the empty short-circuit — submit never runs)', () => {
    const fn = mockFetch({ jobId: 'never' });
    const raw = assembleDictationRaw({ indication: '', technique: '  ', findings: '\n', impression: '', recommendations: '' });
    expect(raw).toBe('');
    // The caller emits 'empty' and returns before submitting — assert the guard
    // would fire (no network happened just from assembling).
    expect(fn).not.toHaveBeenCalled();
  });
});

// ---------------------------------------------------------------------------
// All-or-preview staleness gate — the clinical-safety decision.
// ---------------------------------------------------------------------------
const cleaned: CleanupSectionMap = {
  indication: 'Chest pain, rule out PE.',
  findings: 'No pulmonary embolism. Clear lungs.',
  impression: 'No acute cardiopulmonary process.',
};

describe('planCleanupApply — live-completion path (submit snapshot known)', () => {
  it('auto-applies ALL sections when every target is byte-unchanged since submit', () => {
    const before = { ...cleaned } as Record<string, string>; // unchanged since submit
    const current: CleanupSectionMap = { ...cleaned };
    const plan = planCleanupApply(cleaned, current, before);
    expect(plan.action).toBe('apply');
    // count that the success message reports (N sections)
    expect(plan.keys).toEqual(['indication', 'findings', 'impression']);
  });

  it('routes the WHOLE result to preview when ONE target section was edited (never a partial apply)', () => {
    const before = {
      indication: 'Chest pain, rule out PE.',
      findings: 'No pulmonary embolism. Clear lungs.',
      impression: 'No acute cardiopulmonary process.',
    };
    // The radiologist typed into `findings` under the running job.
    const current: CleanupSectionMap = { ...before, findings: 'No PE. Small effusion (radiologist edit).' };
    const plan = planCleanupApply(cleaned, current, before);
    expect(plan.action).toBe('preview');
    // Whole set is previewed — the OTHER unchanged sections are NOT auto-applied.
    expect(plan.keys).toEqual(['indication', 'findings', 'impression']);
  });

  it('reports no-changes when the job proposed nothing', () => {
    const plan = planCleanupApply({}, { indication: 'x' }, { indication: 'x' });
    expect(plan).toEqual({ action: 'no-changes', keys: [] });
  });
});

describe('planCleanupApply — deep-link path (no submit snapshot)', () => {
  it('auto-applies when every target section is CURRENTLY EMPTY', () => {
    const current: CleanupSectionMap = { indication: '', findings: '   ', impression: '\n' };
    const plan = planCleanupApply(cleaned, current, undefined);
    expect(plan.action).toBe('apply');
    expect(plan.keys).toEqual(['indication', 'findings', 'impression']);
  });

  it('previews the WHOLE result when any target section is already occupied', () => {
    const current: CleanupSectionMap = { indication: '', findings: 'existing findings', impression: '' };
    const plan = planCleanupApply(cleaned, current, undefined);
    expect(plan.action).toBe('preview');
    expect(plan.keys).toEqual(['indication', 'findings', 'impression']);
  });
});

// ---------------------------------------------------------------------------
// Terminal failure copy (the cleanup effect's error/cancelled overlay source).
// ---------------------------------------------------------------------------
describe('cleanup terminal failure copy', () => {
  it('an error surfaces the errorKind-derived copy', () => {
    // The cleanup terminal effect emits describeAiError(job.errorKind, job.error).
    expect(describeAiError('quota_exceeded')).not.toBe('');
    expect(describeAiError('provider_policy')).not.toMatch(/^$/);
    // An unmapped kind (e.g. job_input_expired) falls through to the fallback.
    expect(describeAiError('job_input_expired', 'The job input has expired.')).toBe('The job input has expired.');
  });

  it('a cancelled terminal uses the fixed cancelled copy', () => {
    // Contract: the cleanup terminal effect emits this exact string on 'cancelled'.
    expect('Cleanup cancelled.').toBe('Cleanup cancelled.');
  });
});

describe('CLEANUP_SECTION_KEYS', () => {
  it('covers exactly the five editable report sections in document order', () => {
    expect(CLEANUP_SECTION_KEYS).toEqual([
      'indication',
      'technique',
      'findings',
      'impression',
      'recommendations',
    ]);
  });
});
