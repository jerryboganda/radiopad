// Shared rulebook status helpers. Previously these were copy-pasted into the
// rulebooks list, detail, and visual-editor pages; centralised here so the
// Draft / In review / Approved / Deprecated mapping (and its `.badge` variant)
// stays consistent across the module. Status may arrive as the backend enum
// index (0..3) or its string label.

import type { Rulebook } from '@/lib/api';

const LABELS = ['Draft', 'In review', 'Approved', 'Deprecated'] as const;
const BADGES = ['', 'warn', 'ok', 'danger'] as const;

/** Human-readable status label, e.g. `2` or `"Approved"` → `"Approved"`. */
export function statusLabel(status: Rulebook['status']): string {
  if (typeof status === 'string') return status;
  return LABELS[status] ?? String(status);
}

/**
 * `.badge` modifier class for a status: '' (Draft) | 'warn' (In review) |
 * 'ok' (Approved) | 'danger' (Deprecated). Accepts the numeric enum or a
 * string label.
 */
export function statusBadge(status: Rulebook['status']): string {
  const idx = typeof status === 'number'
    ? status
    : LABELS.findIndex((l) => l.toLowerCase() === status.toLowerCase().replace(/_/g, ' '));
  return BADGES[idx] ?? '';
}

/**
 * Compact relative time for "updated …" meta, e.g. "2d ago". Falls back to a
 * locale date for anything older than ~4 weeks, and returns '' for missing or
 * unparseable input so callers can omit the field gracefully.
 */
export function relativeTime(iso?: string | null): string {
  if (!iso) return '';
  const then = new Date(iso).getTime();
  if (Number.isNaN(then)) return '';
  const secs = Math.round((Date.now() - then) / 1000);
  if (secs < 45) return 'just now';
  const mins = Math.round(secs / 60);
  if (mins < 60) return `${mins}m ago`;
  const hrs = Math.round(mins / 60);
  if (hrs < 24) return `${hrs}h ago`;
  const days = Math.round(hrs / 24);
  if (days < 28) return `${days}d ago`;
  return new Date(iso).toLocaleDateString();
}
