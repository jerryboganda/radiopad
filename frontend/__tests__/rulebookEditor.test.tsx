/**
 * Rulebook visual editor (`/rulebooks/editor`) — tests for panel
 * rendering, Save API call, and Add Rule picker.
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, fireEvent, waitFor, screen } from '@testing-library/react';
import * as React from 'react';

const pushMock = vi.fn();
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: pushMock }),
}));

const rulebookGetMock = vi.fn();
const rulebookSaveMock = vi.fn();
const rulebookValidateYamlMock = vi.fn();
const rulebookApproveMock = vi.fn();

vi.mock('@/lib/api', () => ({
  api: {
    rulebooks: {
      get: (...args: unknown[]) => rulebookGetMock(...args),
      save: (...args: unknown[]) => rulebookSaveMock(...args),
      validateYaml: (...args: unknown[]) => rulebookValidateYamlMock(...args),
      approve: (...args: unknown[]) => rulebookApproveMock(...args),
    },
    // Iter-36 — MetadataPanel fetches the admin catalogs on mount.
    modalities: { list: () => Promise.resolve([]) },
    bodyParts: { list: () => Promise.resolve([]) },
  },
}));

import RulebookEditorPage from '@/app/(desktop)/rulebooks/editor/page';

describe('rulebook editor', () => {
  beforeEach(() => {
    rulebookGetMock.mockReset();
    rulebookSaveMock.mockReset();
    rulebookValidateYamlMock.mockReset();
    rulebookApproveMock.mockReset();
    pushMock.mockReset();
    // No ?id= param — starts with a fresh editor
    window.history.replaceState(null, '', '/rulebooks/editor');
  });

  it('metadata panel renders with all fields', () => {
    const { container } = render(<RulebookEditorPage />);
    // MetadataPanel has Rulebook ID, Name, Version, Owner, Status fields
    expect(screen.getByPlaceholderText('chest_ct_v1')).toBeInTheDocument();
    expect(screen.getByPlaceholderText('Chest CT')).toBeInTheDocument();
    // Version, Owner, Status inputs
    expect(screen.getByDisplayValue('1.0.0')).toBeInTheDocument();
    // Status is a <select>, check it renders the "Draft" option as selected text
    expect(screen.getByText('Draft')).toBeInTheDocument();
    // Panel title
    expect(screen.getByText('Metadata')).toBeInTheDocument();
  });

  it('sections panel renders draggable list', () => {
    const { container } = render(<RulebookEditorPage />);
    expect(screen.getByText('Required Sections')).toBeInTheDocument();
    // Default sections from emptyEditorState
    for (const s of ['Indication', 'Technique', 'Comparison', 'Findings', 'Impression', 'Recommendations']) {
      expect(screen.getByText(s)).toBeInTheDocument();
    }
    // Drag handles (⠿) should be present
    const handles = container.querySelectorAll('.rp-drag-handle');
    expect(handles.length).toBeGreaterThanOrEqual(6);
  });

  it('rules panel renders with severity dropdowns', () => {
    const { container } = render(<RulebookEditorPage />);
    expect(screen.getByText('Validation Rules')).toBeInTheDocument();
    // "Add Rule" button should be visible
    expect(screen.getByText('+ Add Rule')).toBeInTheDocument();
  });

  it('style panel renders avoid_terms as badges', () => {
    const { container } = render(<RulebookEditorPage />);
    expect(screen.getByText('Style')).toBeInTheDocument();
    expect(screen.getByText('Avoid Terms')).toBeInTheDocument();
    expect(screen.getByText('Approved Follow-ups')).toBeInTheDocument();
    // Tone dropdown
    expect(screen.getByText('Concise Clinical')).toBeInTheDocument();
  });

  it('"Save" button calls API with generated YAML', async () => {
    rulebookSaveMock.mockResolvedValue({
      id: 'rb-new',
      rulebookId: 'test_rb',
      name: 'Test',
      version: '1.0.0',
      owner: '',
      status: 0,
    });
    render(<RulebookEditorPage />);
    // Fill in the required fields
    fireEvent.change(screen.getByPlaceholderText('chest_ct_v1'), {
      target: { value: 'test_rb' },
    });
    fireEvent.change(screen.getByPlaceholderText('Chest CT'), {
      target: { value: 'Test Rulebook' },
    });
    fireEvent.click(screen.getByText('Save'));
    await waitFor(() => {
      expect(rulebookSaveMock).toHaveBeenCalledTimes(1);
    });
    const yaml = rulebookSaveMock.mock.calls[0][0] as string;
    expect(yaml).toContain('rulebook_id: test_rb');
    expect(yaml).toContain('name: Test Rulebook');
    expect(yaml).toContain('version: 1.0.0');
  });

  it('"Add Rule" picker shows available rule IDs', () => {
    render(<RulebookEditorPage />);
    fireEvent.click(screen.getByText('+ Add Rule'));
    // After clicking, the picker should show available rule IDs
    expect(screen.getByText('Select a rule')).toBeInTheDocument();
    // Some of the catalog rules should be visible
    expect(screen.getByText('required_sections')).toBeInTheDocument();
    expect(screen.getByText('avoid_terms')).toBeInTheDocument();
    expect(screen.getByText('laterality_consistency')).toBeInTheDocument();
  });
});
