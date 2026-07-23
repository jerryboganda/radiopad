import { describe, it, expect } from 'vitest';
import { STALE_AFTER_MS, isInboxStale, selectListUiState } from '@/lib/notifications';

// NOTIF-010 — the pure state→UI-state decision the inbox page renders from.
// Factored out of the component so the Skeleton / EmptyState / ErrorState /
// stale-banner matrix is testable without a full render.

describe('selectListUiState', () => {
  it('shows the error state first, even while still loading', () => {
    expect(selectListUiState({ loading: true, error: true, count: 0 })).toBe('error');
    expect(selectListUiState({ loading: false, error: true, count: 3 })).toBe('error');
  });

  it('shows loading when not errored and still fetching', () => {
    expect(selectListUiState({ loading: true, error: false, count: 0 })).toBe('loading');
  });

  it('shows empty for a loaded, error-free, zero-row list', () => {
    expect(selectListUiState({ loading: false, error: false, count: 0 })).toBe('empty');
  });

  it('shows the list when rows are present', () => {
    expect(selectListUiState({ loading: false, error: false, count: 5 })).toBe('ready');
  });
});

describe('isInboxStale', () => {
  const now = 10 * 60_000;

  it('is never stale while the stream is open', () => {
    expect(
      isInboxStale({ streamOpen: true, online: false, lastFetchAt: 0, now }),
    ).toBe(false);
  });

  it('is stale when the stream is down and the browser is offline', () => {
    expect(
      isInboxStale({ streamOpen: false, online: false, lastFetchAt: now, now }),
    ).toBe(true);
  });

  it('is not stale when the stream is down but the last fetch was recent and online', () => {
    expect(
      isInboxStale({ streamOpen: false, online: true, lastFetchAt: now - 30_000, now }),
    ).toBe(false);
  });

  it('is stale when the stream is down and the last fetch is older than the threshold', () => {
    expect(
      isInboxStale({
        streamOpen: false,
        online: true,
        lastFetchAt: now - (STALE_AFTER_MS + 1_000),
        now,
      }),
    ).toBe(true);
  });

  it('is not stale before any fetch has happened (no false alarm on first paint)', () => {
    expect(
      isInboxStale({ streamOpen: false, online: true, lastFetchAt: null, now }),
    ).toBe(false);
  });
});
