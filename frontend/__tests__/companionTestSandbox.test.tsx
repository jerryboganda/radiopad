import { describe, it, expect } from 'vitest';
import { render, screen, fireEvent, act } from '@testing-library/react';
import React from 'react';
import CompanionTestSandbox from '../components/companion/CompanionTestSandbox';
import { CompanionProvider } from '../components/companion/CompanionContext';
import { getSectionEditor } from '../lib/editor/sectionEditorRegistry';

describe('CompanionTestSandbox', () => {
  it('renders Findings and Impression practice fields and command guide', () => {
    render(
      <CompanionProvider>
        <CompanionTestSandbox />
      </CompanionProvider>
    );

    expect(screen.getByText(/Practice Findings/i)).toBeInTheDocument();
    expect(screen.getByText(/Practice Impression/i)).toBeInTheDocument();
    expect(screen.getByText(/Voice commands/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /clear practice text/i })).toBeInTheDocument();
  });

  it('registers practice sections with sectionEditorRegistry and inserts text', () => {
    render(
      <CompanionProvider>
        <CompanionTestSandbox />
      </CompanionProvider>
    );

    const findingsEditor = getSectionEditor('findings');
    expect(findingsEditor).toBeDefined();

    act(() => {
      findingsEditor?.insertAtCursor('Lungs are clear bilaterally.');
    });

    const textarea = screen.getByPlaceholderText(/Dictate or type sample findings/i) as HTMLTextAreaElement;
    expect(textarea.value).toContain('Lungs are clear bilaterally.');
  });

  it('clears practice fields when clicking Clear practice text', () => {
    render(
      <CompanionProvider>
        <CompanionTestSandbox />
      </CompanionProvider>
    );

    const findingsEditor = getSectionEditor('findings');
    act(() => {
      findingsEditor?.insertAtCursor('Lungs are clear.');
    });

    const clearBtn = screen.getByRole('button', { name: /clear practice text/i });
    act(() => {
      fireEvent.click(clearBtn);
    });

    const textarea = screen.getByPlaceholderText(/Dictate or type sample findings/i) as HTMLTextAreaElement;
    expect(textarea.value).toBe('');
  });
});
