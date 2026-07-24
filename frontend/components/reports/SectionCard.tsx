'use client';

// RC-02/03 section card — one report section rendered as a white card with an
// icon+title header row, provenance chips ("✨ generated" + "Requires review"
// while AI text is unacknowledged), a ⋮ actions menu, and an optional footer
// action row (Accept / Undo for generated content). The editor itself is
// passed as children so the existing SectionEditor / textarea wiring is
// untouched.
import { useEffect, useRef, useState, type ReactNode } from 'react';
import { MoreVertical, AlertTriangle, X } from 'lucide-react';

export interface SectionCardMenuItem {
  label: string;
  onClick: () => void;
  disabled?: boolean;
}

export interface SectionCardProps {
  sectionKey: string;
  title: string;
  icon?: ReactNode;
  /** True while the section holds unreviewed AI text (drives the chips). */
  generated?: boolean;
  /** Extra chips (rendered after the provenance chips). */
  chips?: ReactNode;
  menuItems?: SectionCardMenuItem[];
  /**
   * When set, renders an X at the top-right that hides this section for the
   * current report. Only wire this up for OPTIONAL sections that are commonly
   * left blank — never for a section the radiologist has to type into, and
   * never in a way that can hide text which still gets exported (see the
   * caller's empty-only guard in ReportClient).
   */
  onDismiss?: () => void;
  /**
   * RC-04 — briefly flash this whole card in a validation severity colour so a
   * "Jump to …" click shows which section the finding covers. Driven from
   * ReportClient state (not a DOM class) so it survives this card's frequent
   * re-renders.
   */
  flash?: 'blocker' | 'warning' | 'info';
  /** Footer action row (Accept / Undo per RC-03). */
  actions?: ReactNode;
  children: ReactNode;
}

export default function SectionCard(p: SectionCardProps) {
  const [menuOpen, setMenuOpen] = useState(false);
  const menuRef = useRef<HTMLDivElement | null>(null);

  // Close the ⋮ menu on outside click / Escape.
  useEffect(() => {
    if (!menuOpen) return;
    const onDocClick = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) setMenuOpen(false);
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setMenuOpen(false);
    };
    document.addEventListener('mousedown', onDocClick);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onDocClick);
      document.removeEventListener('keydown', onKey);
    };
  }, [menuOpen]);

  const hasMenu = (p.menuItems ?? []).length > 0;

  return (
    <section
      className={`rp-sectioncard rp-sectioncard-${p.sectionKey}${p.flash ? ` is-flash tone-${p.flash}` : ''}`}
      data-section={p.sectionKey}
      id={p.sectionKey === 'findings' ? 'rp-findings-section' : undefined}
      aria-label={p.title}
    >
      <header className="rp-sectioncard-head">
        <span className="rp-sectioncard-icon" aria-hidden>{p.icon}</span>
        <h3 className="rp-sectioncard-title">{p.title}</h3>
        <div className="rp-sectioncard-chips">
          {p.generated && (
            <>
              <span className="badge ai">✨ generated</span>
              <span className="badge warn">
                <AlertTriangle size={11} aria-hidden /> Requires review
              </span>
            </>
          )}
          {p.chips}
        </div>
        {hasMenu && (
          <div className="rp-menu" ref={menuRef}>
            <button
              className="icon-btn rp-sectioncard-menu-btn"
              type="button"
              aria-haspopup="menu"
              aria-expanded={menuOpen}
              aria-label={`${p.title} actions`}
              onClick={() => setMenuOpen((v) => !v)}
            >
              <MoreVertical size={15} aria-hidden />
            </button>
            {menuOpen && (
              <div className="rp-menu-popover" role="menu">
                {(p.menuItems ?? []).map((item) => (
                  <button
                    key={item.label}
                    type="button"
                    role="menuitem"
                    className="rp-menu-item"
                    disabled={item.disabled}
                    onClick={() => {
                      setMenuOpen(false);
                      item.onClick();
                    }}
                  >
                    {item.label}
                  </button>
                ))}
              </div>
            )}
          </div>
        )}
        {p.onDismiss && (
          <button
            type="button"
            className="rp-critpanel-dismiss"
            onClick={p.onDismiss}
            aria-label={`Hide ${p.title} for this report`}
            title="Hide for this report"
          >
            <X size={15} aria-hidden />
          </button>
        )}
      </header>

      <div className="rp-sectioncard-body">{p.children}</div>

      {p.actions && <footer className="rp-sectioncard-actions">{p.actions}</footer>}
    </section>
  );
}
