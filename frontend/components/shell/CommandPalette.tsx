'use client';

/**
 * Global command palette (RC topbar "Search / Cmd+K").
 * v1 scope: navigate to any visible module + jump to a recent report by
 * accession / modality / body part. Opens via the topbar search field or
 * Ctrl/Cmd+K; full keyboard support (arrows, Enter, Escape).
 */

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { Search, FileText, CornerDownLeft } from 'lucide-react';
import { navGroups, type NavItem } from './nav.config';
import { usePermissions, type PermissionKey } from '@/lib/permissions';
import { surfaceAllows } from '@/lib/surface';
import { api, type Report } from '@/lib/api';

interface PaletteEntry {
  key: string;
  kind: 'nav' | 'report';
  label: string;
  hint?: string;
  href: string;
  icon?: NavItem['icon'];
}

export default function CommandPalette({
  open,
  onClose,
}: {
  open: boolean;
  onClose: () => void;
}) {
  const router = useRouter();
  const tNav = useTranslations('nav');
  const tPalette = useTranslations('topbar.palette');
  const { can, loading: permsLoading } = usePermissions();
  const [query, setQuery] = useState('');
  const [cursor, setCursor] = useState(0);
  const [reports, setReports] = useState<Report[]>([]);
  const inputRef = useRef<HTMLInputElement | null>(null);
  const listRef = useRef<HTMLDivElement | null>(null);

  const visible = useCallback(
    (permission?: PermissionKey) => permsLoading || !permission || can(permission),
    [permsLoading, can],
  );

  // Lazy-load recent reports the first time the palette opens.
  useEffect(() => {
    if (!open) return;
    setQuery('');
    setCursor(0);
    const t = window.setTimeout(() => inputRef.current?.focus(), 10);
    api.reports
      .list()
      .then((r) => setReports(r.slice(0, 25)))
      .catch(() => setReports([]));
    return () => window.clearTimeout(t);
  }, [open]);

  const entries = useMemo<PaletteEntry[]>(() => {
    const q = query.trim().toLowerCase();
    const navEntries: PaletteEntry[] = navGroups
      .flatMap((g) =>
        g.items.filter(
          (it) => visible(it.permission) && surfaceAllows(it.surfaces ?? g.surfaces),
        ),
      )
      .map((it) => ({
        key: `nav:${it.href}`,
        kind: 'nav' as const,
        label: tNav(it.labelKey),
        href: it.href,
        icon: it.icon,
      }));
    const reportEntries: PaletteEntry[] = reports.map((r) => ({
      key: `report:${r.id}`,
      kind: 'report' as const,
      label: r.study?.accessionNumber || r.id,
      hint: [r.study?.modality, r.study?.bodyPart, String(r.status)]
        .filter(Boolean)
        .join(' · '),
      href: `/reports/view?id=${encodeURIComponent(r.id)}`,
    }));
    const all = [...navEntries, ...reportEntries];
    if (!q) return [...navEntries, ...reportEntries.slice(0, 5)];
    return all.filter((e) =>
      `${e.label} ${e.hint ?? ''}`.toLowerCase().includes(q),
    );
  }, [query, reports, tNav, visible]);

  useEffect(() => setCursor(0), [query]);

  const go = useCallback(
    (entry: PaletteEntry | undefined) => {
      if (!entry) return;
      onClose();
      router.push(entry.href);
    },
    [onClose, router],
  );

  const onKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setCursor((c) => Math.min(c + 1, entries.length - 1));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setCursor((c) => Math.max(c - 1, 0));
    } else if (e.key === 'Enter') {
      e.preventDefault();
      go(entries[cursor]);
    } else if (e.key === 'Escape') {
      e.preventDefault();
      onClose();
    }
  };

  // Keep the active row in view while arrowing.
  useEffect(() => {
    const el = listRef.current?.querySelector('[data-active="true"]');
    el?.scrollIntoView({ block: 'nearest' });
  }, [cursor]);

  if (!open) return null;

  return (
    <div
      className="rp-palette-backdrop"
      role="presentation"
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div
        className="rp-palette"
        role="dialog"
        aria-modal="true"
        aria-label={tPalette('title')}
      >
        <div className="rp-palette-input-row">
          <Search size={15} aria-hidden />
          <input
            ref={inputRef}
            className="rp-palette-input"
            placeholder={tPalette('placeholder')}
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            onKeyDown={onKeyDown}
            aria-label={tPalette('title')}
          />
          <kbd className="rp-palette-kbd">Esc</kbd>
        </div>
        <div className="rp-palette-list" ref={listRef} role="listbox">
          {entries.length === 0 && (
            <div className="rp-palette-empty">{tPalette('noResults')}</div>
          )}
          {entries.map((entry, i) => {
            const Icon = entry.kind === 'nav' ? entry.icon : undefined;
            return (
              <button
                key={entry.key}
                type="button"
                role="option"
                aria-selected={i === cursor}
                data-active={i === cursor ? 'true' : undefined}
                className={`rp-palette-item ${i === cursor ? 'active' : ''}`}
                onMouseEnter={() => setCursor(i)}
                onClick={() => go(entry)}
              >
                <span className="rp-palette-item-icon" aria-hidden>
                  {Icon ? <Icon /> : <FileText size={14} />}
                </span>
                <span className="rp-palette-item-label">{entry.label}</span>
                {entry.hint && <span className="rp-palette-item-hint">{entry.hint}</span>}
                {i === cursor && (
                  <span className="rp-palette-item-enter" aria-hidden>
                    <CornerDownLeft size={12} />
                  </span>
                )}
              </button>
            );
          })}
        </div>
      </div>
    </div>
  );
}
