// The audio card must play the take through the authenticated client (blob URL), never the server
// filesystem path in `storagePath` — an <audio src> can send neither the API base nor the Bearer header.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';

vi.mock('@/lib/api/reportingClient', () => ({
  getDictationAudioBlob: vi.fn(),
}));

import { getDictationAudioBlob } from '@/lib/api/reportingClient';
import DictationAudioCard from '@/app/(desktop)/reports/[id]/components/DictationAudioCard';

const dictation = {
  id: 'd1',
  reportId: 'r1',
  storagePath: '/app/uploads/dictations/r1/take.webm',
  durationSeconds: 5,
  transcriptionEngine: 'medASR-6gram',
  status: 'Completed',
  transcribedText: 'No acute findings.',
  uploadedAt: '2026-11-22T10:00:00Z',
};

describe('DictationAudioCard playback source', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    URL.createObjectURL = vi.fn(() => 'blob:take-1');
    URL.revokeObjectURL = vi.fn();
  });

  it('plays an authenticated blob URL, not the server storage path', async () => {
    (getDictationAudioBlob as ReturnType<typeof vi.fn>).mockResolvedValue(new Blob(['audio']));
    const { container } = render(<DictationAudioCard dictation={dictation} onAppendToFindings={() => {}} />);

    await waitFor(() => expect(container.querySelector('audio')?.getAttribute('src')).toBe('blob:take-1'));
    expect(getDictationAudioBlob).toHaveBeenCalledWith('r1', 'd1');
  });

  it('says so when the recording cannot be loaded', async () => {
    (getDictationAudioBlob as ReturnType<typeof vi.fn>).mockRejectedValue(new Error('API 404'));
    render(<DictationAudioCard dictation={dictation} onAppendToFindings={() => {}} />);

    expect(await screen.findByTestId('audio-load-error')).toBeInTheDocument();
  });
});
