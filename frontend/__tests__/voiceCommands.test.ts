import { describe, it, expect } from 'vitest';
import { detectCommand, stripCommand, type CommandMatch } from '@/lib/voiceCommands';

describe('voiceCommands — detectCommand', () => {
  // --- generate_impression ---
  it.each([
    'generate impression',
    'Generate Impression',
    'create impression',
    'Create impression',
  ])('matches generate_impression for "%s"', (phrase) => {
    const result = detectCommand(phrase);
    expect(result).not.toBeNull();
    expect(result!.command).toBe('generate_impression');
  });

  // --- make_concise ---
  it.each([
    'make concise',
    'make it concise',
    'rewrite concise',
    'rewrite it concise',
  ])('matches make_concise for "%s"', (phrase) => {
    const result = detectCommand(phrase);
    expect(result).not.toBeNull();
    expect(result!.command).toBe('make_concise');
  });

  // --- make_formal ---
  it.each([
    'make formal',
    'make it formal',
    'rewrite formal',
    'rewrite it formal',
  ])('matches make_formal for "%s"', (phrase) => {
    const result = detectCommand(phrase);
    expect(result).not.toBeNull();
    expect(result!.command).toBe('make_formal');
  });

  // --- patient_friendly ---
  it.each([
    'patient friendly',
    'patient-friendly',
    'patientfriendly',
  ])('matches patient_friendly for "%s"', (phrase) => {
    const result = detectCommand(phrase);
    expect(result).not.toBeNull();
    expect(result!.command).toBe('patient_friendly');
  });

  // --- validate_report ---
  it.each([
    'validate report',
    'validate the report',
  ])('matches validate_report for "%s"', (phrase) => {
    const result = detectCommand(phrase);
    expect(result).not.toBeNull();
    expect(result!.command).toBe('validate_report');
  });

  // --- cleanup_dictation ---
  it.each([
    'clean up dictation',
    'clean up the dictation',
    'cleanup dictation',
    'cleanup the dictation',
  ])('matches cleanup_dictation for "%s"', (phrase) => {
    const result = detectCommand(phrase);
    expect(result).not.toBeNull();
    expect(result!.command).toBe('cleanup_dictation');
  });

  // --- navigation & editing commands (PowerScribe/Dragon parity) ---
  it.each([
    ['next field', 'next_field'],
    ['next section', 'next_field'],
    ['previous field', 'previous_field'],
    ['prior section', 'previous_field'],
    ['last field', 'previous_field'],
    ['new paragraph', 'new_paragraph'],
    ['new line', 'new_line'],
    ['scratch that', 'undo_that'],
    ['undo that', 'undo_that'],
    ['open sign and send', 'open_sign'],
    ['sign and send', 'open_sign'],
    ['sign the report', 'open_sign'],
  ] as const)('matches "%s" → %s', (phrase, command) => {
    const result = detectCommand(phrase);
    expect(result).not.toBeNull();
    expect(result!.command).toBe(command);
  });

  it.each([
    ['go to findings', 'findings'],
    ['go to the impression', 'impression'],
    ['jump to impressions', 'impression'],
    ['go to technique', 'technique'],
    ['go to comparison', 'comparison'],
    ['go to indication', 'indication'],
    ['go to history', 'indication'],
    ['jump to recommendations', 'recommendations'],
  ] as const)('maps "%s" to section key %s', (phrase, sectionKey) => {
    const result = detectCommand(phrase);
    expect(result).not.toBeNull();
    expect(result!.command).toBe('go_to_section');
    expect(result!.sectionKey).toBe(sectionKey);
  });

  it('does not treat prose mentioning sections as navigation', () => {
    expect(detectCommand('The findings are stable.')).toBeNull();
    expect(detectCommand('Comparison with the prior study shows no change.')).toBeNull();
  });

  // --- command at end of transcript ---
  it('detects a command in the last sentence of a longer transcript', () => {
    const transcript =
      'There is a 2 cm lesion in the right lobe of the liver. Generate impression';
    const result = detectCommand(transcript);
    expect(result).not.toBeNull();
    expect(result!.command).toBe('generate_impression');
  });

  // --- no false positives ---
  it.each([
    'The liver is unremarkable.',
    'No acute findings.',
    'Impression is unremarkable.',
    'The patient is friendly and cooperative.',
    'The report was validated yesterday.',
    'The dictation was clean.',
    '',
    '   ',
  ])('returns null for normal text: "%s"', (text) => {
    expect(detectCommand(text)).toBeNull();
  });
});

describe('voiceCommands — stripCommand', () => {
  it('removes the command phrase from the transcript', () => {
    const match: CommandMatch = {
      command: 'generate_impression',
      matchedPhrase: 'generate impression',
    };
    const result = stripCommand(
      'There is a 2 cm lesion. generate impression',
      match,
    );
    expect(result).toBe('There is a 2 cm lesion.');
  });

  it('handles command at the beginning of transcript', () => {
    const match: CommandMatch = {
      command: 'validate_report',
      matchedPhrase: 'validate the report',
    };
    const result = stripCommand('validate the report', match);
    expect(result).toBe('');
  });

  it('cleans up double spaces after stripping', () => {
    const match: CommandMatch = {
      command: 'make_concise',
      matchedPhrase: 'make it concise',
    };
    const result = stripCommand('some text make it concise please', match);
    expect(result).toBe('some text please');
  });

  it('returns original transcript when phrase is not found', () => {
    const match: CommandMatch = {
      command: 'make_formal',
      matchedPhrase: 'make it formal',
    };
    const result = stripCommand('totally different text', match);
    expect(result).toBe('totally different text');
  });
});
