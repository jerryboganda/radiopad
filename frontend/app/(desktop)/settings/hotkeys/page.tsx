'use client';

// RC-10 — Hotkey customization. Grouped, two-column table of every registered
// shortcut with an editable "Current binding" pill: click the pencil, the pill
// enters a Listening state, and the next chord you press becomes the new
// binding (Esc cancels). Conflicts (the same chord on two enabled actions)
// are outlined in red and block saving. Bindings are stored on this device.

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import Link from 'next/link';
import { Search, Pencil, RotateCcw, ArrowLeft, Keyboard } from 'lucide-react';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import Banner from '@/components/ui/Banner';
import {
  HOTKEYS,
  HOTKEY_CATEGORIES,
  bindingFromKeyboardEvent,
  findConflicts,
  formatBinding,
  getBindings,
  normalizeBinding,
  resetAll,
  setBinding,
  type HotkeyCategory,
  type HotkeyDef,
} from '@/lib/hotkeys';
import {
  FOOT_PEDAL_ACTIONS,
  FOOT_PEDAL_CHANGE_EVENT,
  getFootPedalBindings,
  resetFootPedalBindings,
  setFootPedalBinding,
  type FootPedalBindings,
} from '@/lib/dictation/footPedal';

type CategoryFilter = 'all' | HotkeyCategory;

function defaultDraft(): Record<string, string> {
  const out: Record<string, string> = {};
  for (const def of HOTKEYS) out[def.id] = normalizeBinding(def.defaultBinding);
  return out;
}

function Kbd({ binding }: { binding: string }) {
  return (
    <kbd className="inline-block rounded border border-rule bg-canvas px-1.5 py-0.5 font-mono text-[11px] text-ink-soft whitespace-nowrap">
      {formatBinding(binding)}
    </kbd>
  );
}

export default function HotkeysPage() {
  // Draft = unsaved working copy; saved = what localStorage currently holds.
  const [draft, setDraft] = useState<Record<string, string>>(defaultDraft);
  const [saved, setSaved] = useState<Record<string, string>>(defaultDraft);
  const [search, setSearch] = useState('');
  const [category, setCategory] = useState<CategoryFilter>('all');
  const [recordingId, setRecordingId] = useState<string | null>(null);
  const [savedToast, setSavedToast] = useState(false);
  const toastTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Load persisted bindings after mount (prerender has no localStorage).
  useEffect(() => {
    const current = getBindings();
    setDraft(current);
    setSaved(current);
    return () => {
      if (toastTimerRef.current) clearTimeout(toastTimerRef.current);
    };
  }, []);

  // Recording mode: capture the next chord anywhere on the page.
  useEffect(() => {
    if (!recordingId) return;
    const onKey = (e: KeyboardEvent) => {
      e.preventDefault();
      e.stopPropagation();
      if (e.key === 'Escape') {
        setRecordingId(null);
        return;
      }
      const binding = bindingFromKeyboardEvent(e);
      if (!binding) return; // modifier-only — keep listening
      setDraft((d) => ({ ...d, [recordingId]: binding }));
      setRecordingId(null);
    };
    window.addEventListener('keydown', onKey, true);
    return () => window.removeEventListener('keydown', onKey, true);
  }, [recordingId]);

  const conflicts = useMemo(() => findConflicts(draft), [draft]);
  const conflictIds = useMemo(() => {
    const s = new Set<string>();
    for (const c of conflicts) for (const id of c.ids) s.add(id);
    return s;
  }, [conflicts]);

  const dirty = useMemo(
    () => HOTKEYS.some((h) => (draft[h.id] ?? '') !== (saved[h.id] ?? '')),
    [draft, saved],
  );

  const save = useCallback(() => {
    if (conflicts.length > 0) return;
    for (const def of HOTKEYS) {
      setBinding(def.id, draft[def.id] ?? def.defaultBinding);
    }
    const current = getBindings();
    setDraft(current);
    setSaved(current);
    setSavedToast(true);
    if (toastTimerRef.current) clearTimeout(toastTimerRef.current);
    toastTimerRef.current = setTimeout(() => setSavedToast(false), 5000);
  }, [conflicts.length, draft]);

  const reset = useCallback(() => {
    resetAll();
    const fresh = defaultDraft();
    setDraft(fresh);
    setSaved(fresh);
    setRecordingId(null);
  }, []);

  const matches = useCallback(
    (def: HotkeyDef) => {
      if (category !== 'all' && def.category !== category) return false;
      const q = search.trim().toLowerCase();
      if (!q) return true;
      const binding = draft[def.id] ?? def.defaultBinding;
      return (
        def.label.toLowerCase().includes(q) ||
        (def.description ?? '').toLowerCase().includes(q) ||
        formatBinding(binding).toLowerCase().includes(q) ||
        def.category.toLowerCase().includes(q)
      );
    },
    [category, search, draft],
  );

  const liveGroups = useMemo(
    () =>
      HOTKEY_CATEGORIES.map((cat) => ({
        category: cat,
        defs: HOTKEYS.filter((h) => h.implemented && h.category === cat && matches(h)),
      })).filter((g) => g.defs.length > 0),
    [matches],
  );

  const comingSoon = useMemo(() => HOTKEYS.filter((h) => !h.implemented && matches(h)), [matches]);
  const nothingMatches = liveGroups.length === 0 && comingSoon.length === 0;

  function statusFor(def: HotkeyDef): { label: string; tone: string } {
    if (conflictIds.has(def.id)) return { label: 'Conflict', tone: 'blocked' };
    const current = normalizeBinding(draft[def.id] ?? def.defaultBinding);
    if (current !== normalizeBinding(def.defaultBinding)) return { label: 'Custom', tone: 'ai' };
    return { label: 'Default', tone: 'ready' };
  }

  function renderRow(def: HotkeyDef) {
    const status = statusFor(def);
    const recording = recordingId === def.id;
    const conflict = conflictIds.has(def.id);
    const current = draft[def.id] ?? normalizeBinding(def.defaultBinding);
    const isCustom = normalizeBinding(current) !== normalizeBinding(def.defaultBinding);
    return (
      <tr key={def.id}>
        <td>
          <div style={{ fontWeight: 600 }}>{def.label}</div>
          {def.description && <div className="rp-page-sub">{def.description}</div>}
        </td>
        <td>
          <span className="rp-chip">{def.category}</span>
          <div className="rp-page-sub" style={{ marginTop: 4 }}>
            {def.scope === 'editor' ? 'Report editor' : 'Everywhere'}
          </div>
        </td>
        <td>
          <Kbd binding={def.defaultBinding} />
        </td>
        <td>
          <div style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
            <button
              type="button"
              className={`ghost${recording ? ' active' : ''}${conflict ? ' border-danger text-danger' : ''}`}
              style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}
              aria-label={
                recording
                  ? `Listening for a new shortcut for ${def.label}. Press keys now, or Esc to cancel.`
                  : `Change shortcut for ${def.label} (currently ${formatBinding(current)})`
              }
              onClick={() => setRecordingId(recording ? null : def.id)}
            >
              {recording ? (
                <>
                  <span className="rp-spinner sm" aria-hidden />
                  Listening…
                </>
              ) : (
                <>
                  <span className="font-mono text-xs">{formatBinding(current)}</span>
                  <Pencil size={13} strokeWidth={1.8} aria-hidden />
                </>
              )}
            </button>
            {isCustom && !recording && (
              <button
                type="button"
                className="subtle"
                title="Restore the default binding"
                aria-label={`Restore default binding for ${def.label}`}
                onClick={() =>
                  setDraft((d) => ({ ...d, [def.id]: normalizeBinding(def.defaultBinding) }))
                }
              >
                <RotateCcw size={13} strokeWidth={1.8} aria-hidden />
              </button>
            )}
          </div>
        </td>
        <td>
          <span className="status-badge" data-tone={status.tone}>
            {status.label}
          </span>
        </td>
      </tr>
    );
  }

  return (
    <Container>
      <PageHeader
        title="Hotkey customization"
        description="Customize keyboard shortcuts to speed up your reporting workflow. Changes apply on this device only."
        secondaryActions={
          <>
            <Link
              href="/settings"
              className="ghost"
              style={{ textDecoration: 'none', display: 'inline-flex', alignItems: 'center', gap: 6 }}
            >
              <ArrowLeft size={15} strokeWidth={1.8} aria-hidden />
              Settings
            </Link>
            <button type="button" className="ghost" onClick={reset}>
              <RotateCcw size={14} strokeWidth={1.8} aria-hidden style={{ marginRight: 6, verticalAlign: -2 }} />
              Reset to defaults
            </button>
          </>
        }
        primaryAction={
          <button
            type="button"
            className="primary"
            onClick={save}
            disabled={!dirty || conflicts.length > 0}
          >
            Save changes
          </button>
        }
      />

      {savedToast && (
        <Banner tone="success" title="Hotkeys saved." onDismiss={() => setSavedToast(false)}>
          Your custom shortcuts are now active on this device.
        </Banner>
      )}

      {conflicts.length > 0 && (
        <Banner tone="danger" title="Shortcut conflict">
          {conflicts.map((c) => {
            const names = c.ids
              .map((id) => HOTKEYS.find((h) => h.id === id)?.label ?? id)
              .join(' and ');
            return (
              <div key={c.binding}>
                <strong>{formatBinding(c.binding)}</strong> is assigned to both {names}. Choose a
                different key combination — nothing is saved while a conflict remains.
              </div>
            );
          })}
        </Banner>
      )}

      {/* ── Search + category filter ─────────────────────────────── */}
      <div className="rp-filter-bar rp-anim-fade-in-up">
        <div className="rp-search" style={{ position: 'relative', flex: '1 1 240px' }}>
          <Search
            size={14}
            strokeWidth={1.8}
            aria-hidden
            className="text-ink-soft"
            style={{ position: 'absolute', left: 10, top: '50%', transform: 'translateY(-50%)' }}
          />
          <input
            type="search"
            className="rp-input"
            style={{ width: '100%', paddingLeft: 30 }}
            placeholder="Search actions or shortcuts…"
            aria-label="Search actions or shortcuts"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </div>
        {(['all', ...HOTKEY_CATEGORIES] as CategoryFilter[]).map((c) => (
          <button
            key={c}
            type="button"
            className={`ghost${category === c ? ' active' : ''}`}
            onClick={() => setCategory(c)}
          >
            {c === 'all' ? 'All categories' : c}
          </button>
        ))}
      </div>

      {/* ── Legend ───────────────────────────────────────────────── */}
      <div
        className="rp-panel rp-anim-fade-in-up"
        style={{ display: 'flex', gap: 14, alignItems: 'center', flexWrap: 'wrap', marginBottom: 20 }}
      >
        <span className="rp-page-sub" style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
          <Keyboard size={14} strokeWidth={1.8} aria-hidden />
          Click a binding to edit, then press the new keys. Esc cancels while listening.
        </span>
        <span className="status-badge" data-tone="ready">Default</span>
        <span className="status-badge" data-tone="ai">Custom</span>
        <span className="status-badge" data-tone="blocked">Conflict</span>
      </div>

      {nothingMatches ? (
        <div className="rp-panel">
          <p className="rp-page-sub">
            No shortcuts match “{search}”. Try a different search or category.
          </p>
        </div>
      ) : (
        <>
          {/* ── Grouped shortcut tables, two columns ─────────────── */}
          <div className="rp-grid-2">
            {liveGroups.map((group) => (
              <div key={group.category} className="rp-panel rp-anim-fade-in-up">
                <div className="rp-panel-title">
                  {group.category}{' '}
                  <span className="rp-page-sub">({group.defs.length})</span>
                </div>
                <div className="table-wrap">
                  <table className="data-table">
                    <thead>
                      <tr>
                        <th>Action</th>
                        <th>Category</th>
                        <th>Default</th>
                        <th>Current binding</th>
                        <th>Status</th>
                      </tr>
                    </thead>
                    <tbody>{group.defs.map(renderRow)}</tbody>
                  </table>
                </div>
              </div>
            ))}
          </div>

          {/* ── Coming soon (registered but not yet wired up) ────── */}
          {comingSoon.length > 0 && (
            <div className="rp-panel rp-anim-fade-in-up" style={{ marginTop: 20 }}>
              <div className="rp-panel-title">Coming soon</div>
              <p className="rp-page-sub" style={{ marginBottom: 10 }}>
                These shortcuts are planned but not active yet, so they can’t be customized.
              </p>
              <div className="table-wrap">
                <table className="data-table">
                  <thead>
                    <tr>
                      <th>Action</th>
                      <th>Category</th>
                      <th>Scope</th>
                      <th>Planned binding</th>
                      <th>Status</th>
                    </tr>
                  </thead>
                  <tbody>
                    {comingSoon.map((def) => (
                      <tr key={def.id}>
                        <td>
                          <div style={{ fontWeight: 600 }}>{def.label}</div>
                          {def.description && <div className="rp-page-sub">{def.description}</div>}
                        </td>
                        <td>
                          <span className="rp-chip">{def.category}</span>
                        </td>
                        <td>
                          <span className="rp-chip">
                            {def.scope === 'editor' ? 'Report editor' : 'Everywhere'}
                          </span>
                        </td>
                        <td>
                          <Kbd binding={def.defaultBinding} />
                        </td>
                        <td>
                          <span className="status-badge" data-tone="muted">
                            Coming soon
                          </span>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </>
      )}
      <FootPedalPanel />
    </Container>
  );
}

/* ── DESK-020 — foot pedal ────────────────────────────────────────────── */

/**
 * Foot-pedal bindings. Pedals in keyboard mode emit plain key events
 * (typically F13–F24), so capture stores the raw KeyboardEvent.code — no
 * modifier chords. Works while the RadioPad window has focus.
 */
function FootPedalPanel() {
  const [bindings, setBindings] = useState<FootPedalBindings>(getFootPedalBindings);
  const [capturing, setCapturing] = useState<keyof FootPedalBindings | null>(null);

  useEffect(() => {
    const sync = () => setBindings(getFootPedalBindings());
    window.addEventListener(FOOT_PEDAL_CHANGE_EVENT, sync);
    window.addEventListener('storage', sync);
    return () => {
      window.removeEventListener(FOOT_PEDAL_CHANGE_EVENT, sync);
      window.removeEventListener('storage', sync);
    };
  }, []);

  useEffect(() => {
    if (!capturing) return;
    const onKey = (e: KeyboardEvent) => {
      e.preventDefault();
      e.stopPropagation();
      if (e.key === 'Escape') {
        setCapturing(null);
        return;
      }
      setFootPedalBinding(capturing, e.code);
      setCapturing(null);
    };
    window.addEventListener('keydown', onKey, true);
    return () => window.removeEventListener('keydown', onKey, true);
  }, [capturing]);

  return (
    <div className="rp-panel" style={{ marginTop: 16 }}>
      <div className="rp-panel-title">Foot pedal</div>
      <p className="rp-page-sub" style={{ marginTop: 4 }}>
        Transcription pedals in keyboard mode (Infinity, Olympus, Philips, VEC…) send ordinary
        key presses — usually F13–F24. Press a pedal while capturing to bind it. Pedals work
        while the RadioPad window is focused.
      </p>
      <div className="table-wrap">
        <table className="rp-table">
          <thead>
            <tr>
              <th>Action</th>
              <th>Pedal key</th>
              <th aria-label="Controls" />
            </tr>
          </thead>
          <tbody>
            {FOOT_PEDAL_ACTIONS.map(({ key, label, description }) => (
              <tr key={key}>
                <td>
                  <div className="text-ink" style={{ fontWeight: 600 }}>{label}</div>
                  <div className="rp-page-sub">{description}</div>
                </td>
                <td>
                  {capturing === key ? (
                    <span className="status-badge" data-tone="review">Press a pedal… (Esc cancels)</span>
                  ) : bindings[key] ? (
                    <Kbd binding={bindings[key]} />
                  ) : (
                    <span className="status-badge" data-tone="muted">Not bound</span>
                  )}
                </td>
                <td>
                  <div className="rp-row" style={{ gap: 6, justifyContent: 'flex-end' }}>
                    <button className="subtle" onClick={() => setCapturing(key)}>
                      <Pencil size={13} aria-hidden /> Capture
                    </button>
                    {bindings[key] && (
                      <button className="subtle" onClick={() => setFootPedalBinding(key, '')}>
                        Clear
                      </button>
                    )}
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="rp-row" style={{ marginTop: 8 }}>
        <button className="subtle" onClick={() => resetFootPedalBindings()}>
          <RotateCcw size={13} aria-hidden /> Reset to F13/F14/F15
        </button>
      </div>
    </div>
  );
}
