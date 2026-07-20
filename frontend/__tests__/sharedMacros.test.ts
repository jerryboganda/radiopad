// PRD RPT-021 — shared (tenant / subspecialty) macros layered under the
// radiologist's own device-local snippets. The precedence rule is the point:
// a personal snippet must always beat a departmental macro on the same
// trigger, or an admin edit could silently change what someone dictates.
import { describe, it, expect, beforeEach } from 'vitest';
import {
  resolveTrigger,
  _resetSharedMacros,
  _seedSharedMacros,
} from '@/lib/sharedMacros';
import { saveSnippet, _resetSnippets, SNIPPET_STORAGE_KEY } from '@/lib/snippets';
import type { SharedMacro } from '@/lib/api';

function macro(trigger: string, body: string, scope: 'Tenant' | 'Subspecialty', subspecialty = ''): SharedMacro {
  return {
    id: `${scope}-${trigger}`,
    trigger,
    body,
    description: '',
    scope,
    subspecialty,
    updatedAt: new Date(0).toISOString(),
  };
}

describe('resolveTrigger — macro precedence', () => {
  beforeEach(() => {
    _resetSharedMacros();
    window.localStorage.removeItem(SNIPPET_STORAGE_KEY);
    _resetSnippets();
  });

  it('returns null when nothing matches', () => {
    expect(resolveTrigger('nlchest')).toBeNull();
    expect(resolveTrigger('   ')).toBeNull();
  });

  it('finds a tenant-wide macro', () => {
    _seedSharedMacros([macro('nlchest', 'Workspace normal chest.', 'Tenant')]);
    expect(resolveTrigger('nlchest')).toEqual({ body: 'Workspace normal chest.', source: 'tenant' });
  });

  it('prefers a subspecialty macro over the tenant-wide one', () => {
    _seedSharedMacros([
      macro('nlchest', 'Workspace normal chest.', 'Tenant'),
      macro('nlchest', 'Neuro normal chest.', 'Subspecialty', 'Neuro'),
    ]);
    expect(resolveTrigger('nlchest')).toEqual({ body: 'Neuro normal chest.', source: 'subspecialty' });
  });

  it("a personal snippet beats every shared macro", () => {
    _seedSharedMacros([
      macro('nlchest', 'Workspace normal chest.', 'Tenant'),
      macro('nlchest', 'Neuro normal chest.', 'Subspecialty', 'Neuro'),
    ]);
    saveSnippet({ trigger: 'nlchest', body: 'My own normal chest.' });
    expect(resolveTrigger('nlchest')).toEqual({ body: 'My own normal chest.', source: 'personal' });
  });

  it('matches triggers case-insensitively and ignores surrounding space', () => {
    _seedSharedMacros([macro('NLChest', 'Workspace normal chest.', 'Tenant')]);
    expect(resolveTrigger('  nlchest ')?.source).toBe('tenant');
  });

  it('leaves unrelated triggers alone when macros exist', () => {
    _seedSharedMacros([macro('nlchest', 'Workspace normal chest.', 'Tenant')]);
    expect(resolveTrigger('nlabdo')).toBeNull();
  });
});
