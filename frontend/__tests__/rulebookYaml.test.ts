/**
 * Rulebook YAML conversion utility — tests for rulebookToYaml,
 * yamlToRulebookEditor, round-trip, and empty state.
 */
import { describe, it, expect } from 'vitest';
import {
  rulebookToYaml,
  yamlToRulebookEditor,
  emptyEditorState,
  type RulebookEditorState,
} from '@/lib/rulebookYaml';

describe('rulebookToYaml', () => {
  it('produces valid YAML structure', () => {
    const state: RulebookEditorState = {
      ...emptyEditorState(),
      rulebook_id: 'chest_ct_v1',
      name: 'Chest CT',
      version: '1.0.0',
      owner: 'thoracic',
      status: 'draft',
      applies_to: {
        modalities: ['CT'],
        body_parts: ['Chest'],
        report_types: ['diagnostic'],
      },
      style: {
        tone: 'concise_clinical',
        impression_max_bullets: 5,
        avoid_terms: ['unremarkable', 'essentially'],
        approved_followups: ['CT follow-up in 3 months'],
      },
      required_sections: [
        { name: 'Indication', required: true },
        { name: 'Findings', required: true },
        { name: 'Impression', required: true },
        { name: 'Comparison', required: false },
      ],
      rules: [
        { id: 'required_sections', severity: 'blocker', description: 'All sections must be present' },
        { id: 'avoid_terms', severity: 'warning', description: 'Check for forbidden terms' },
      ],
      prompt_blocks: [
        { key: 'system_prompt', text: 'You are a radiology assistant.' },
      ],
    };

    const yaml = rulebookToYaml(state);

    // Top-level scalars
    expect(yaml).toContain('rulebook_id: chest_ct_v1');
    expect(yaml).toContain('name: Chest CT');
    expect(yaml).toContain('version: 1.0.0');
    expect(yaml).toContain('owner: thoracic');
    expect(yaml).toContain('status: draft');

    // applies_to lists
    expect(yaml).toContain('modalities:');
    expect(yaml).toContain('- CT');
    expect(yaml).toContain('body_parts:');
    expect(yaml).toContain('- Chest');
    expect(yaml).toContain('report_types:');
    expect(yaml).toContain('- diagnostic');

    // style
    expect(yaml).toContain('tone: concise_clinical');
    expect(yaml).toContain('impression_max_bullets: 5');
    expect(yaml).toContain('avoid_terms:');
    expect(yaml).toContain('- unremarkable');
    expect(yaml).toContain('- essentially');

    // required_sections (only required=true)
    expect(yaml).toContain('required_sections:');
    expect(yaml).toContain('- Indication');
    expect(yaml).toContain('- Findings');
    expect(yaml).toContain('- Impression');
    // Comparison is not required, so it should not be in required_sections list
    expect(yaml).not.toMatch(/required_sections:[\s\S]*- Comparison/);

    // rules
    expect(yaml).toContain('rules:');
    expect(yaml).toContain('- id: required_sections');
    expect(yaml).toContain('severity: blocker');
    expect(yaml).toContain('- id: avoid_terms');

    // prompt_blocks
    expect(yaml).toContain('prompt_blocks:');
    expect(yaml).toContain('system_prompt: |');
    expect(yaml).toContain('You are a radiology assistant.');

    // Ends with newline
    expect(yaml.endsWith('\n')).toBe(true);
  });
});

describe('yamlToRulebookEditor', () => {
  it('parses a known YAML string correctly', () => {
    const yaml = `rulebook_id: brain_mri_v2
name: Brain MRI
version: 2.0.0
owner: neuro
status: approved
applies_to:
  modalities:
    - MR
  body_parts:
    - Head
  report_types:
    - diagnostic
style:
  tone: verbose_clinical
  impression_max_bullets: 3
  avoid_terms:
    - normal
  approved_followups:
    - MRI follow-up in 6 months
required_sections:
  - Indication
  - Technique
  - Findings
  - Impression
rules:
  - id: findings_not_empty
    severity: blocker
    description: Findings cannot be blank
  - id: spelling_check
    severity: info
    description: Run spellcheck
`;

    const state = yamlToRulebookEditor(yaml);

    expect(state.rulebook_id).toBe('brain_mri_v2');
    expect(state.name).toBe('Brain MRI');
    expect(state.version).toBe('2.0.0');
    expect(state.owner).toBe('neuro');
    expect(state.status).toBe('approved');

    expect(state.applies_to.modalities).toEqual(['MR']);
    expect(state.applies_to.body_parts).toEqual(['Head']);
    expect(state.applies_to.report_types).toEqual(['diagnostic']);

    expect(state.style.tone).toBe('verbose_clinical');
    expect(state.style.impression_max_bullets).toBe(3);
    expect(state.style.avoid_terms).toEqual(['normal']);
    expect(state.style.approved_followups).toEqual(['MRI follow-up in 6 months']);

    expect(state.required_sections).toEqual([
      { name: 'Indication', required: true },
      { name: 'Technique', required: true },
      { name: 'Findings', required: true },
      { name: 'Impression', required: true },
    ]);

    expect(state.rules).toHaveLength(2);
    expect(state.rules[0]).toEqual({
      id: 'findings_not_empty',
      severity: 'blocker',
      description: 'Findings cannot be blank',
    });
    expect(state.rules[1]).toEqual({
      id: 'spelling_check',
      severity: 'info',
      description: 'Run spellcheck',
    });
  });
});

describe('round-trip', () => {
  it('yamlToRulebookEditor(rulebookToYaml(state)) preserves all fields', () => {
    const original: RulebookEditorState = {
      rulebook_id: 'round_trip_test',
      name: 'Round Trip',
      version: '3.0.0',
      owner: 'qa-team',
      status: 'in_review',
      applies_to: {
        modalities: ['CT', 'MR'],
        body_parts: ['Chest', 'Abdomen'],
        report_types: ['screening'],
      },
      style: {
        tone: 'educational',
        impression_max_bullets: 7,
        avoid_terms: ['normal', 'unremarkable'],
        approved_followups: ['Follow-up CT', 'Follow-up MRI'],
      },
      required_sections: [
        { name: 'Indication', required: true },
        { name: 'Technique', required: true },
        { name: 'Findings', required: true },
        { name: 'Impression', required: true },
        { name: 'Recommendations', required: false },
      ],
      rules: [
        { id: 'required_sections', severity: 'blocker', description: 'Sections check' },
        { id: 'avoid_terms', severity: 'warning', description: 'Term check' },
        { id: 'grammar_check', severity: 'info', description: 'Grammar' },
      ],
      prompt_blocks: [],
    };

    const yaml = rulebookToYaml(original);
    const restored = yamlToRulebookEditor(yaml);

    expect(restored.rulebook_id).toBe(original.rulebook_id);
    expect(restored.name).toBe(original.name);
    expect(restored.version).toBe(original.version);
    expect(restored.owner).toBe(original.owner);
    expect(restored.status).toBe(original.status);

    expect(restored.applies_to.modalities).toEqual(original.applies_to.modalities);
    expect(restored.applies_to.body_parts).toEqual(original.applies_to.body_parts);
    expect(restored.applies_to.report_types).toEqual(original.applies_to.report_types);

    expect(restored.style.tone).toBe(original.style.tone);
    expect(restored.style.impression_max_bullets).toBe(original.style.impression_max_bullets);
    expect(restored.style.avoid_terms).toEqual(original.style.avoid_terms);
    expect(restored.style.approved_followups).toEqual(original.style.approved_followups);

    // required_sections: only required=true ones appear in YAML, so
    // the restored state will only have those
    const requiredOnly = original.required_sections.filter((s) => s.required);
    expect(restored.required_sections).toEqual(requiredOnly);

    expect(restored.rules).toEqual(original.rules);
  });
});

describe('empty state', () => {
  it('produces a valid minimal YAML', () => {
    const state = emptyEditorState();
    const yaml = rulebookToYaml(state);

    expect(yaml).toContain('rulebook_id:');
    expect(yaml).toContain('name:');
    expect(yaml).toContain('version: 1.0.0');
    expect(yaml).toContain('status: draft');
    expect(yaml).toContain('applies_to:');
    expect(yaml).toContain('style:');
    expect(yaml).toContain('required_sections:');
    expect(yaml.endsWith('\n')).toBe(true);

    // Should be parseable without errors
    const parsed = yamlToRulebookEditor(yaml);
    expect(parsed.version).toBe('1.0.0');
    expect(parsed.status).toBe('draft');
  });
});
