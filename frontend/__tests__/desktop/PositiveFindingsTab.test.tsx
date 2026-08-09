import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, act } from '@testing-library/react';
import React from 'react';
import PositiveFindingsTab from '@/app/(desktop)/reports/[id]/components/PositiveFindingsTab';
import DictationAudioCard from '@/app/(desktop)/reports/[id]/components/DictationAudioCard';
import type { DictationAudioDto } from '@/lib/api/reportingClient';

beforeEach(() => {
  window.HTMLMediaElement.prototype.play = vi.fn().mockImplementation(() => Promise.resolve());
  window.HTMLMediaElement.prototype.pause = vi.fn();
});

const sampleDictation1: DictationAudioDto = {
  id: 'dictation-1',
  reportId: 'report-101',
  storagePath: '/api/v1/audio/dictation-1.wav',
  durationSeconds: 45.5,
  transcriptionEngine: 'medASR-6gram',
  status: 'Completed',
  transcribedText: 'Acute right lower lobe pneumonia with moderate pleural effusion.',
  uploadedAt: new Date().toISOString(),
};

const sampleDictation2: DictationAudioDto = {
  id: 'dictation-2',
  reportId: 'report-101',
  storagePath: '/api/v1/audio/dictation-2.wav',
  durationSeconds: 30.0,
  transcriptionEngine: 'UBAG Gemini',
  status: 'Completed',
  transcribedText: 'No fracture or dislocation identified in the right wrist.',
  uploadedAt: new Date().toISOString(),
};

describe('DictationAudioCard component', () => {
  it('renders senior radiologist header, engine badge, timestamp and transcribed text', () => {
    const onAppend = vi.fn();
    render(<DictationAudioCard dictation={sampleDictation1} onAppendToFindings={onAppend} />);

    expect(screen.getByText('Senior Radiologist Dictation')).toBeDefined();
    expect(screen.getByTestId('engine-badge').textContent).toContain('medASR (4.4% WER)');
    expect(screen.getByTestId('transcribed-text-input')).toBeDefined();
    expect((screen.getByTestId('transcribed-text-input') as HTMLTextAreaElement).value).toBe(
      'Acute right lower lobe pneumonia with moderate pleural effusion.'
    );
  });

  it('toggles audio play/pause state when play button is clicked', async () => {
    const onAppend = vi.fn();
    render(<DictationAudioCard dictation={sampleDictation1} onAppendToFindings={onAppend} />);

    const playBtn = screen.getByTestId('audio-play-pause-btn');
    expect(playBtn.getAttribute('aria-label')).toBe('Play');

    await act(async () => {
      fireEvent.click(playBtn);
    });
    expect(playBtn).toBeDefined();
  });

  it('changes playback speed option', () => {
    const onAppend = vi.fn();
    render(<DictationAudioCard dictation={sampleDictation1} onAppendToFindings={onAppend} />);

    const speed15 = screen.getByTestId('speed-option-1.5');
    fireEvent.click(speed15);
    expect(speed15.className).toContain('bg-cyan-600');
  });

  it('allows editing transcript and appends to findings', () => {
    const onAppend = vi.fn();
    render(<DictationAudioCard dictation={sampleDictation1} onAppendToFindings={onAppend} />);

    const textarea = screen.getByTestId('transcribed-text-input') as HTMLTextAreaElement;
    fireEvent.change(textarea, { target: { value: 'Updated: Acute pneumonia resolved.' } });
    expect(textarea.value).toBe('Updated: Acute pneumonia resolved.');

    const appendBtn = screen.getByTestId('append-to-findings-btn');
    fireEvent.click(appendBtn);

    expect(onAppend).toHaveBeenCalledTimes(1);
    expect(onAppend).toHaveBeenCalledWith('Updated: Acute pneumonia resolved.');
  });

  it('invokes retranscribe callback with selected engine', () => {
    const onAppend = vi.fn();
    const onRetranscribe = vi.fn();
    render(
      <DictationAudioCard
        dictation={sampleDictation1}
        onAppendToFindings={onAppend}
        onRetranscribe={onRetranscribe}
      />
    );

    const engineSelect = screen.getByTestId('engine-select') as HTMLSelectElement;
    fireEvent.change(engineSelect, { target: { value: 'UBAG Gemini' } });

    const retranscribeBtn = screen.getByTestId('retranscribe-btn');
    fireEvent.click(retranscribeBtn);

    expect(onRetranscribe).toHaveBeenCalledTimes(1);
    expect(onRetranscribe).toHaveBeenCalledWith('dictation-1', 'UBAG Gemini');
  });
});

describe('PositiveFindingsTab component', () => {
  it('renders empty notice when no dictations exist', () => {
    render(
      <PositiveFindingsTab
        reportId="report-101"
        dictations={[]}
        onAppendToFindings={vi.fn()}
      />
    );

    expect(screen.getByText('Positive Findings & Mobile Dictations')).toBeDefined();
    expect(screen.getByTestId('dictations-count-badge').textContent).toBe('0');
    expect(screen.getByTestId('empty-dictations-notice')).toBeDefined();
  });

  it('renders chronological list of dictations', () => {
    render(
      <PositiveFindingsTab
        reportId="report-101"
        dictations={[sampleDictation1, sampleDictation2]}
        onAppendToFindings={vi.fn()}
      />
    );

    expect(screen.getByTestId('dictations-count-badge').textContent).toBe('2');
    const cards = screen.getAllByTestId('dictation-audio-card');
    expect(cards.length).toBe(2);
  });

  it('displays real-time toast notification on radiopad:dictation-uploaded event', () => {
    render(
      <PositiveFindingsTab
        reportId="report-101"
        dictations={[]}
        onAppendToFindings={vi.fn()}
      />
    );

    expect(screen.queryByTestId('realtime-dictation-toast')).toBeNull();

    act(() => {
      window.dispatchEvent(
        new CustomEvent('radiopad:dictation-uploaded', {
          detail: { reportId: 'report-101', dictation: sampleDictation1 },
        })
      );
    });

    expect(screen.getByTestId('realtime-dictation-toast')).toBeDefined();
    expect(screen.getByText('New Audio Dictation received from Mobile')).toBeDefined();
    expect(screen.getAllByTestId('dictation-audio-card').length).toBe(1);
  });

  it('updates dictation transcript in real time on radiopad:transcription-completed event', () => {
    render(
      <PositiveFindingsTab
        reportId="report-101"
        dictations={[sampleDictation1]}
        onAppendToFindings={vi.fn()}
      />
    );

    const textareaBefore = screen.getByTestId('transcribed-text-input') as HTMLTextAreaElement;
    expect(textareaBefore.value).toBe('Acute right lower lobe pneumonia with moderate pleural effusion.');

    act(() => {
      window.dispatchEvent(
        new CustomEvent('radiopad:transcription-completed', {
          detail: {
            reportId: 'report-101',
            dictationId: 'dictation-1',
            text: 'Live updated transcript from SignalR hub.',
            status: 'Completed',
          },
        })
      );
    });

    const textareaAfter = screen.getByTestId('transcribed-text-input') as HTMLTextAreaElement;
    expect(textareaAfter.value).toBe('Live updated transcript from SignalR hub.');
  });
});
