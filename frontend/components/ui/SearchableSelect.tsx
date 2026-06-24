'use client';

/**
 * SearchableSelect — a controlled combobox that replaces a native `<select>`
 * where the option list is long enough to want a type-to-filter search box
 * (e.g. the report editor's 20+-entry Rulebook picker).
 *
 * It keeps the same `value` / `onChange(value | null)` contract as the
 * `<select>`s it replaces, so call sites swap in place. The popover pattern
 * (autofocus search, click-outside + Escape to close) mirrors
 * `components/shell/ProfileMenu.tsx`. Styling lives in `app/globals.css`
 * (`.rp-combobox*`) — see `docs/02-design/design.md`.
 */

import { useEffect, useId, useRef, useState } from 'react';

export interface SearchableSelectOption {
  value: string;
  label: string;
  /** Extra tokens folded into the filter (e.g. modalities, body parts). */
  searchText?: string;
  disabled?: boolean;
}

export interface SearchableSelectProps {
  options: SearchableSelectOption[];
  /** Selected value; `null`/`''` means nothing selected. */
  value: string | null;
  onChange: (value: string | null) => void;
  /** Trigger text when nothing is selected. */
  placeholder?: string;
  searchPlaceholder?: string;
  /** Prepend a clearable "— none —" row that emits `null`. */
  includeNone?: boolean;
  noneLabel?: string;
  /** Shown when the filter matches nothing. */
  emptyLabel?: string;
  /** Wired to the trigger so an external `<label htmlFor>` can target it. */
  id?: string;
  ariaLabel?: string;
  disabled?: boolean;
  className?: string;
}

const NONE_VALUE = '';

export default function SearchableSelect({
  options,
  value,
  onChange,
  placeholder = 'Select…',
  searchPlaceholder = 'Search…',
  includeNone = false,
  noneLabel = '— none —',
  emptyLabel = 'No matches',
  id,
  ariaLabel,
  disabled = false,
  className,
}: SearchableSelectProps) {
  const reactId = useId();
  const baseId = id ?? `combobox-${reactId}`;
  const listId = `${baseId}-list`;

  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const [activeIndex, setActiveIndex] = useState(-1);

  const rootRef = useRef<HTMLDivElement | null>(null);
  const searchRef = useRef<HTMLInputElement | null>(null);
  const triggerRef = useRef<HTMLButtonElement | null>(null);
  const listRef = useRef<HTMLUListElement | null>(null);

  const allRows: SearchableSelectOption[] = includeNone
    ? [{ value: NONE_VALUE, label: noneLabel }, ...options]
    : options;

  const q = query.trim().toLowerCase();
  const rows = q
    ? allRows.filter((r) => `${r.label} ${r.searchText ?? ''}`.toLowerCase().includes(q))
    : allRows;

  const selected = options.find((o) => o.value === value);
  const triggerLabel = selected ? selected.label : placeholder;
  const isPlaceholder = !selected;

  function close() {
    setOpen(false);
    setQuery('');
    setActiveIndex(-1);
  }

  function openMenu() {
    if (disabled) return;
    setOpen(true);
    setActiveIndex(-1);
  }

  function select(row: SearchableSelectOption) {
    if (row.disabled) return;
    onChange(row.value === NONE_VALUE ? null : row.value);
    close();
    triggerRef.current?.focus();
  }

  function move(dir: 1 | -1) {
    const enabled = rows.map((r, i) => (r.disabled ? -1 : i)).filter((i) => i >= 0);
    if (enabled.length === 0) return;
    const pos = enabled.indexOf(activeIndex);
    const nextPos = pos === -1 ? (dir === 1 ? 0 : enabled.length - 1) : Math.min(Math.max(pos + dir, 0), enabled.length - 1);
    setActiveIndex(enabled[nextPos]);
  }

  function onSearchKeyDown(e: React.KeyboardEvent<HTMLInputElement>) {
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      move(1);
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      move(-1);
    } else if (e.key === 'Enter') {
      e.preventDefault();
      if (activeIndex >= 0 && rows[activeIndex] && !rows[activeIndex].disabled) select(rows[activeIndex]);
      else if (rows.length === 1 && !rows[0].disabled) select(rows[0]);
    }
  }

  // Autofocus the search input each time the popover opens.
  useEffect(() => {
    if (open) searchRef.current?.focus();
  }, [open]);

  // Keep the highlighted option in view during keyboard navigation.
  useEffect(() => {
    if (!open || activeIndex < 0) return;
    listRef.current?.querySelector<HTMLElement>(`[data-index="${activeIndex}"]`)?.scrollIntoView({ block: 'nearest' });
  }, [activeIndex, open]);

  // Close on outside click or Escape (mirrors ProfileMenu).
  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      if (rootRef.current && !rootRef.current.contains(e.target as Node)) close();
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        close();
        triggerRef.current?.focus();
      }
    };
    window.addEventListener('mousedown', onDown);
    window.addEventListener('keydown', onKey);
    return () => {
      window.removeEventListener('mousedown', onDown);
      window.removeEventListener('keydown', onKey);
    };
  }, [open]);

  const activeId = open && activeIndex >= 0 ? `${baseId}-opt-${activeIndex}` : undefined;

  return (
    <div className={`rp-combobox${className ? ` ${className}` : ''}`} ref={rootRef}>
      <button
        ref={triggerRef}
        type="button"
        id={baseId}
        className="rp-combobox-trigger"
        role="combobox"
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-controls={listId}
        aria-label={ariaLabel}
        disabled={disabled}
        onClick={() => (open ? close() : openMenu())}
        onKeyDown={(e) => {
          if (!open && (e.key === 'ArrowDown' || e.key === 'Enter' || e.key === ' ')) {
            e.preventDefault();
            openMenu();
          }
        }}
      >
        <span className="rp-combobox-value" data-placeholder={isPlaceholder ? 'true' : undefined}>
          {triggerLabel}
        </span>
        <span className="rp-combobox-caret" aria-hidden="true">▾</span>
      </button>

      {open && (
        <div className="rp-combobox-panel">
          <input
            ref={searchRef}
            type="text"
            className="rp-combobox-search"
            placeholder={searchPlaceholder}
            value={query}
            aria-controls={listId}
            aria-autocomplete="list"
            aria-activedescendant={activeId}
            onChange={(e) => {
              setQuery(e.target.value);
              setActiveIndex(-1);
            }}
            onKeyDown={onSearchKeyDown}
          />
          <ul id={listId} ref={listRef} role="listbox" className="rp-combobox-list">
            {rows.length === 0 ? (
              <li className="rp-combobox-empty">{emptyLabel}</li>
            ) : (
              rows.map((row, i) => (
                <li
                  key={row.value === NONE_VALUE ? '__none__' : row.value}
                  id={`${baseId}-opt-${i}`}
                  data-index={i}
                  role="option"
                  aria-selected={(value ?? NONE_VALUE) === row.value}
                  aria-disabled={row.disabled || undefined}
                  className={`rp-combobox-option${i === activeIndex ? ' is-active' : ''}`}
                  onMouseEnter={() => !row.disabled && setActiveIndex(i)}
                  onMouseDown={(e) => {
                    e.preventDefault();
                    select(row);
                  }}
                >
                  {row.label}
                </li>
              ))
            )}
          </ul>
        </div>
      )}
    </div>
  );
}
