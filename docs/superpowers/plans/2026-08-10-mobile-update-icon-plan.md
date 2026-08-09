# Sleek Icon-Only Mobile Update Check Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor `<MobileUpdateCheck />` into a compact icon-only button with a sleek update popover.

**Architecture:** 
- Convert `MobileUpdateCheck` button to a sleek 36px icon button (`RefreshCw` / `DownloadCloud`).
- Implement an absolute popover dropdown for status messages ("Up to date vX.Y.Z", "Update available", "Download & install").
- Maintain clean layout alignment in `CompanionTopbar`.

**Tech Stack:** React, Lucide Icons, Tailwind CSS / Vanilla CSS.

---

### Task 1: Refactor MobileUpdateCheck component to sleek icon button with popover

**Files:**
- Modify: [e:/Projects/RadioPad/RADIOPAD/frontend/components/companion/MobileUpdateCheck.tsx](file:///e:/Projects/RadioPad/RADIOPAD/frontend/components/companion/MobileUpdateCheck.tsx)

- [ ] **Step 1: Update MobileUpdateCheck implementation**

Refactor `MobileUpdateCheck.tsx` to render an icon button with popover state:
```tsx
'use client';

import { useCallback, useState, useRef, useEffect } from 'react';
import { DownloadCloud, RefreshCw, CheckCircle2, AlertCircle, X } from 'lucide-react';
import { isMobileSurface } from '@/lib/surface';
import {
  checkMobileUpdate,
  APP_VERSION,
  RELEASES_URL,
  type MobileUpdateInfo,
} from '@/lib/mobileUpdate';

type Phase = 'idle' | 'checking' | 'uptodate' | 'available' | 'error';

export default function MobileUpdateCheck() {
  const [phase, setPhase] = useState<Phase>('idle');
  const [info, setInfo] = useState<MobileUpdateInfo | null>(null);
  const [open, setOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  const check = useCallback(async () => {
    setPhase('checking');
    setOpen(true);
    try {
      const result = await checkMobileUpdate();
      setInfo(result);
      setPhase(result.updateAvailable ? 'available' : 'uptodate');
    } catch {
      setPhase('error');
    }
  }, []);

  useEffect(() => {
    function handleClickOutside(e: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        setOpen(false);
      }
    }
    if (open) {
      document.addEventListener('mousedown', handleClickOutside);
      return () => document.removeEventListener('mousedown', handleClickOutside);
    }
  }, [open]);

  if (!isMobileSurface) return null;

  return (
    <div className="relative inline-block" ref={containerRef}>
      <button
        type="button"
        className={`relative w-9 h-9 flex items-center justify-center rounded-xl border border-[var(--border)] bg-[var(--bg-panel)] text-[var(--text-muted)] hover:text-[var(--text)] transition-colors ${
          phase === 'available' ? 'border-blue-500 text-blue-400 bg-blue-500/10' : ''
        }`}
        onClick={check}
        disabled={phase === 'checking'}
        title={`Check for updates (v${APP_VERSION})`}
        aria-label="Check for updates"
      >
        <RefreshCw
          size={16}
          className={phase === 'checking' ? 'animate-spin text-blue-400' : ''}
        />
        {phase === 'available' && (
          <span className="absolute -top-1 -right-1 w-2.5 h-2.5 rounded-full bg-blue-500 ring-2 ring-[var(--bg-app)] animate-pulse" />
        )}
      </button>

      {open && phase !== 'checking' && (
        <div className="absolute right-0 top-11 z-50 w-64 p-3 rounded-2xl bg-[var(--bg-panel)] border border-[var(--border)] shadow-xl text-xs flex flex-col gap-2.5 animate-in fade-in slide-in-from-top-2 duration-150">
          <div className="flex items-center justify-between font-semibold text-[var(--text-strong)]">
            <span className="flex items-center gap-1.5">
              {phase === 'available' && <DownloadCloud size={14} className="text-blue-400" />}
              {phase === 'uptodate' && <CheckCircle2 size={14} className="text-emerald-400" />}
              {phase === 'error' && <AlertCircle size={14} className="text-amber-400" />}
              {phase === 'available' ? 'Update available' : phase === 'uptodate' ? 'Up to date' : 'Check failed'}
            </span>
            <button
              type="button"
              onClick={() => setOpen(false)}
              className="p-1 text-[var(--text-muted)] hover:text-[var(--text)] rounded-lg"
            >
              <X size={12} />
            </button>
          </div>

          {phase === 'available' && info && (
            <>
              <p className="text-[var(--text-muted)] leading-relaxed">
                Version <strong className="text-[var(--text-strong)]">v{info.latest}</strong> is available. (You have v{info.current})
              </p>
              <a
                className="w-full py-2 px-3 rounded-xl bg-blue-600 hover:bg-blue-500 text-white font-medium flex items-center justify-center gap-1.5 transition-colors text-center"
                href={info.downloadUrl ?? RELEASES_URL}
                target="_blank"
                rel="noreferrer"
              >
                <DownloadCloud size={14} />
                {info.downloadUrl ? 'Download & install' : 'View release'}
              </a>
            </>
          )}

          {phase === 'uptodate' && (
            <p className="text-[var(--text-muted)]">
              You are running the latest version (<strong className="text-[var(--text-strong)]">v{APP_VERSION}</strong>).
            </p>
          )}

          {phase === 'error' && (
            <p className="text-[var(--text-muted)]">
              Could not check for updates. Tap the icon to retry.
            </p>
          )}
        </div>
      )}
    </div>
  );
}
```

- [ ] **Step 2: Typecheck & verify compilation**

Run: `pnpm --filter frontend run typecheck`
Expected: PASS cleanly (0 errors).

---

### Task 2: Commit and push updated release

- [ ] **Step 1: Commit icon button refactor**

Commit changes: `fix(mobile): refactor check for updates to sleek icon button in topbar`

- [ ] **Step 2: Tag and push version release**

Tag `v0.1.153` and push to trigger fresh GitHub build.
