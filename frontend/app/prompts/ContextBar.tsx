'use client';

import type { Rulebook } from '@/lib/api';
import { statusLabel, statusBadge } from '@/lib/rulebookStatus';
import SearchableSelect, { type SearchableSelectOption } from '@/components/ui/SearchableSelect';

export interface ContextBarProps {
  rulebooks: Rulebook[];
  activeId: string | null;
  active: Rulebook | null;
  dirtyCount: number;
  onSelect: (id: string | null) => void;
}

export default function ContextBar({ rulebooks, activeId, active, dirtyCount, onSelect }: ContextBarProps) {
  const options: SearchableSelectOption[] = rulebooks.map((rb) => ({
    value: rb.id,
    label: `${rb.name || rb.rulebookId} (${rb.version})`,
    searchText: `${rb.rulebookId} ${statusLabel(rb.status)}`,
  }));

  return (
    <div className="rp-filter-bar rp-context-bar">
      <div className="rp-context-field">
        <label className="rp-context-label" htmlFor="ps-rulebook-context">
          Rulebook
        </label>
        <SearchableSelect
          id="ps-rulebook-context"
          options={options}
          value={activeId}
          onChange={onSelect}
          placeholder="Select a rulebook…"
          searchPlaceholder="Search rulebooks…"
          ariaLabel="Rulebook context"
        />
      </div>

      {active ? (
        <span className={`badge ${statusBadge(active.status)}`}>{statusLabel(active.status)}</span>
      ) : null}

      <div className="rp-context-spacer" />

      {dirtyCount > 0 ? (
        <span className="rp-chip rp-chip-dirty">
          {dirtyCount} unsaved {dirtyCount === 1 ? 'draft' : 'drafts'}
        </span>
      ) : null}
    </div>
  );
}
