import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import * as React from 'react';

const dictationSettingsGetMock = vi.fn();
const dictationSettingsUpdateMock = vi.fn();

vi.mock('@/lib/api', () => ({
  api: {
    dictationSettings: {
      get: () => dictationSettingsGetMock(),
      update: (body: unknown) => dictationSettingsUpdateMock(body),
    },
  },
}));

vi.mock('next/link', () => ({
  default: ({ href, children, ...rest }: React.PropsWithChildren<{ href: string }>) => (
    <a href={href} {...rest}>{children}</a>
  ),
}));

import AdminDictationSettingsPage from '@/app/(desktop)/admin/dictation-settings/page';

describe('AdminDictationSettingsPage', () => {
  beforeEach(() => {
    dictationSettingsGetMock.mockReset();
    dictationSettingsUpdateMock.mockReset();
  });

  it('renders loading state initially and loads dictation settings with medASR default', async () => {
    dictationSettingsGetMock.mockResolvedValue({
      activeEngine: 'medASR-6gram',
      ubagModel: 'ubag-gemini-audio',
      radiologySystemPrompt: 'You are an expert medical radiology transcription assistant...',
    });

    render(<AdminDictationSettingsPage />);

    expect(screen.getByText('Loading Dictation & Audio AI Settings...')).toBeInTheDocument();

    await waitFor(() => {
      expect(screen.getByTestId('dictation-settings-page')).toBeInTheDocument();
    });

    const medasrRadio = screen.getByTestId('engine-option-medasr').querySelector('input');
    expect(medasrRadio).toBeChecked();

    const ubagSelect = screen.getByTestId('ubag-model-select') as HTMLSelectElement;
    expect(ubagSelect.value).toBe('ubag-gemini-audio');

    const promptTextarea = screen.getByTestId('radiology-system-prompt') as HTMLTextAreaElement;
    expect(promptTextarea.value).toContain('You are an expert medical radiology transcription assistant');
  });

  it('allows changing engine, UBAG model, prompt, and resetting prompt', async () => {
    dictationSettingsGetMock.mockResolvedValue({
      activeEngine: 'medASR-6gram',
      ubagModel: 'ubag-gemini-audio',
      radiologySystemPrompt: 'Original prompt text',
    });

    render(<AdminDictationSettingsPage />);

    await waitFor(() => {
      expect(screen.getByTestId('dictation-settings-page')).toBeInTheDocument();
    });

    // Select UBAG Cloud Provider engine
    const ubagCloudCard = screen.getByTestId('engine-option-ubag-cloud');
    const ubagCloudRadio = ubagCloudCard.querySelector('input')!;
    fireEvent.click(ubagCloudRadio);
    expect(ubagCloudRadio).toBeChecked();

    // Select UBAG ChatGPT Audio dropdown option
    const ubagSelect = screen.getByTestId('ubag-model-select');
    fireEvent.change(ubagSelect, { target: { value: 'ubag-chatgpt-audio' } });
    expect((ubagSelect as HTMLSelectElement).value).toBe('ubag-chatgpt-audio');

    // Edit prompt textarea
    const promptTextarea = screen.getByTestId('radiology-system-prompt');
    fireEvent.change(promptTextarea, { target: { value: 'Custom updated radiology prompt' } });
    expect((promptTextarea as HTMLTextAreaElement).value).toBe('Custom updated radiology prompt');

    // Click Reset to Default button
    const resetButton = screen.getByTestId('reset-prompt-btn');
    fireEvent.click(resetButton);
    expect((promptTextarea as HTMLTextAreaElement).value).toContain('You are an expert medical radiology transcription assistant');
  });

  it('saves updated settings successfully', async () => {
    dictationSettingsGetMock.mockResolvedValue({
      activeEngine: 'medASR-6gram',
      ubagModel: 'ubag-gemini-audio',
      radiologySystemPrompt: 'Default prompt',
    });
    dictationSettingsUpdateMock.mockResolvedValue({
      activeEngine: 'ubag-cloud',
      ubagModel: 'ubag-chatgpt-audio',
      radiologySystemPrompt: 'Custom prompt',
    });

    render(<AdminDictationSettingsPage />);

    await waitFor(() => {
      expect(screen.getByTestId('dictation-settings-page')).toBeInTheDocument();
    });

    const ubagCloudRadio = screen.getByTestId('engine-option-ubag-cloud').querySelector('input')!;
    fireEvent.click(ubagCloudRadio);

    const saveButton = screen.getByTestId('save-settings-btn');
    fireEvent.click(saveButton);

    await waitFor(() => {
      expect(dictationSettingsUpdateMock).toHaveBeenCalledWith({
        activeEngine: 'ubag-cloud',
        ubagModel: 'ubag-gemini-audio',
        radiologySystemPrompt: 'Default prompt',
      });
      expect(screen.getByTestId('save-success-banner')).toBeInTheDocument();
    });
  });

  it('runs live audio transcription test utility', async () => {
    dictationSettingsGetMock.mockResolvedValue({
      activeEngine: 'medASR-6gram',
      ubagModel: 'ubag-gemini-audio',
      radiologySystemPrompt: 'Default prompt',
    });

    render(<AdminDictationSettingsPage />);

    await waitFor(() => {
      expect(screen.getByTestId('dictation-settings-page')).toBeInTheDocument();
    });

    const testInput = screen.getByTestId('test-audio-input');
    fireEvent.change(testInput, { target: { value: 'Chest CT shows clear lungs.' } });

    const runButton = screen.getByTestId('run-test-dictation-btn');
    fireEvent.click(runButton);

    await waitFor(
      () => {
        expect(screen.getByTestId('live-transcription-result')).toBeInTheDocument();
        expect(screen.getByTestId('live-transcription-result')).toHaveTextContent('Chest CT shows clear lungs.');
      },
      { timeout: 2000 }
    );
  });
});
