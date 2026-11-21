'use client';

/**
 * "Check for updates" icon control for the mobile companion app header.
 *
 * Rendered in CompanionTopbar next to ThemeToggle. Shows a clean 36x36px icon button
 * that triggers update checks and opens a sleek popover with version info & download CTA.
 */

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
        className={`relative w-9 h-9 flex items-center justify-center rounded-xl border border-[var(--border,#262c40)] bg-[var(--bg-panel,#131722)] text-[var(--text-muted,#94a3b8)] hover:text-[var(--text,#e2e8f0)] transition-colors ${
          phase === 'available' ? 'border-[var(--accent)] text-[var(--accent)] bg-[var(--bg-selected)]' : ''
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
          <span className="absolute -top-0.5 -right-0.5 w-2.5 h-2.5 rounded-full bg-[var(--accent)] ring-2 ring-[var(--bg-app,#0b0f17)] animate-pulse" />
        )}
      </button>

      {open && phase !== 'checking' && (
        <div className="absolute right-0 top-11 z-50 w-64 p-3.5 rounded-2xl bg-[var(--bg-panel,#131722)] border border-[var(--border,#262c40)] shadow-2xl text-xs flex flex-col gap-2.5">
          <div className="flex items-center justify-between font-semibold text-[var(--text-strong,#f8fafc)]">
            <span className="flex items-center gap-1.5">
              {phase === 'available' && <DownloadCloud size={14} className="text-blue-400" />}
              {phase === 'uptodate' && <CheckCircle2 size={14} className="text-emerald-400" />}
              {phase === 'error' && <AlertCircle size={14} className="text-amber-400" />}
              {phase === 'available' ? 'Update available' : phase === 'uptodate' ? 'Up to date' : 'Check failed'}
            </span>
            <button
              type="button"
              onClick={() => setOpen(false)}
              className="p-1 text-[var(--text-muted,#94a3b8)] hover:text-[var(--text,#e2e8f0)] rounded-lg transition-colors"
            >
              <X size={13} />
            </button>
          </div>

          {phase === 'available' && info && (
            <>
              <p className="text-[var(--text-muted,#94a3b8)] leading-relaxed">
                Version <strong className="text-[var(--text-strong,#f8fafc)]">v{info.latest}</strong> is available. (You have v{info.current})
              </p>
              <a
                className="w-full py-2 px-3 rounded-xl bg-[var(--accent)] hover:opacity-90 text-[var(--accent-fg)] font-medium flex items-center justify-center gap-1.5 transition-colors text-center"
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
            <p className="text-[var(--text-muted,#94a3b8)] leading-relaxed">
              You are running the latest version (<strong className="text-[var(--text-strong,#f8fafc)]">v{APP_VERSION}</strong>).
            </p>
          )}

          {phase === 'error' && (
            <p className="text-[var(--text-muted,#94a3b8)] leading-relaxed">
              Could not check for updates. Tap the icon to retry.
            </p>
          )}
        </div>
      )}
    </div>
  );
}

