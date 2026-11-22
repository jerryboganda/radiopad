// Drift guard: every value of the backend AuditAction enum must have a readable label here, otherwise the audit
// page shows "Action 33" for it.
import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { AUDIT_ACTION_LABEL, auditActionLabel } from '@/lib/auditLabels';

function backendAuditActionIds(): number[] {
  const src = readFileSync(resolve(__dirname, '../../backend/RadioPad.Api/src/RadioPad.Domain/Enums/Enums.cs'), 'utf8');
  const start = src.indexOf('public enum AuditAction');
  const end = src.indexOf('\n}', start);
  const body = src.slice(start, end).replace(/\/\/\/.*$/gm, '').replace(/\/\/.*$/gm, '').replace(/\/\*[\s\S]*?\*\//g, '');
  return [...body.matchAll(/^\s*[A-Za-z]+\s*=\s*(\d+)\s*,/gm)].map((m) => Number(m[1]));
}

describe('audit action labels', () => {
  it('names every backend AuditAction value', () => {
    const ids = backendAuditActionIds();
    expect(ids.length).toBeGreaterThan(50);
    const missing = ids.filter((id) => !(id in AUDIT_ACTION_LABEL));
    expect(missing).toEqual([]);
  });

  it('falls back for unknown numbers and passes strings through', () => {
    expect(auditActionLabel(9999)).toBe('Action 9999');
    expect(auditActionLabel('custom')).toBe('custom');
    expect(auditActionLabel(33)).toBe('Sessions revoked');
  });
});
