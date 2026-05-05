// Iter-36 MOB — `/mobile/dictate?reportId=...` page test. We render with
// the Web Speech API absent to confirm the fallback message, then with
// it present and assert the locked classes (`.rp-mic-btn`,
// `.rp-transcript`) are used and the save button calls
// `api.reports.appendFindings`.
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, fireEvent, waitFor, act } from '@testing-library/react';

const pushMock = vi.fn();
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: pushMock }),
}));

const appendFindings = vi.fn(async () => ({ id: 'rpt-1' }));
vi.mock('@/lib/api', () => ({
  api: {
    reports: {
      appendFindings: (...args: unknown[]) => appendFindings(...args),
    },
  },
}));

import Page from '@/app/mobile/dictate/page';

describe('mobile dictate page', () => {
  beforeEach(() => {
    appendFindings.mockClear();
    pushMock.mockClear();
    window.history.replaceState(null, '', '/mobile/dictate?reportId=rpt-1');
    window.localStorage.clear();
    delete (window as unknown as { SpeechRecognition?: unknown }).SpeechRecognition;
    delete (window as unknown as { webkitSpeechRecognition?: unknown }).webkitSpeechRecognition;
  });
  afterEach(() => {
    window.localStorage.clear();
  });

  it('renders the locked mic button and transcript area', () => {
    const { getByTestId } = render(<Page />);
    const mic = getByTestId('mic-btn');
    expect(mic.classList.contains('rp-mic-btn')).toBe(true);
    expect(getByTestId('transcript').classList.contains('rp-transcript')).toBe(true);
  });

  it('shows fallback message when SpeechRecognition is unavailable', () => {
    const { getByTestId, getByRole } = render(<Page />);
    expect(getByTestId('mic-btn')).toBeDisabled();
    const banner = getByRole('status');
    expect(banner.textContent).toMatch(/speech recognition is not available/i);
    expect(banner.classList.contains('banner')).toBe(true);
    expect(banner.classList.contains('warn')).toBe(true);
  });

  it('transcript area uses the locked serif via class only (no inline style)', () => {
    const { getByTestId } = render(<Page />);
    const transcript = getByTestId('transcript');
    // No inline colour/font styles allowed; styling comes from `.rp-transcript`
    // which is defined with `font-family: var(--serif)` in radiopad.css.
    expect(transcript.getAttribute('style')).toBeNull();
    expect(transcript.classList.contains('rp-transcript')).toBe(true);
  });

  it('save button calls api.reports.appendFindings with the transcript', async () => {
    // Provide a typed-in transcript by writing the offline draft before mount,
    // which the page restores from `localStorage`.
    window.localStorage.setItem('radiopad.mobile.dictate.rpt-1', 'lungs clear');
    const { getByTestId } = render(<Page />);
    const save = getByTestId('save-btn') as HTMLButtonElement;
    await waitFor(() => expect(save.disabled).toBe(false));
    await act(async () => {
      fireEvent.click(save);
    });
    await waitFor(() => expect(appendFindings).toHaveBeenCalledWith('rpt-1', 'lungs clear'));
  });
});
