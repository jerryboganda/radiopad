'use client';

/**
 * RPT-010 — Copy a sanitised plain-text version of the report to the
 * clipboard for paste-into-RIS workflows. Calls
 * `GET /api/reports/{id}/export/text?preview=true` (already exists) so the
 * copy does not flip the report status or write an audit row. After a
 * successful copy we display a brief banner that auto-clears in 30s.
 */

import { useEffect, useRef, useState } from 'react';
import { api } from '@/lib/api';
import StatusBadge from '@/components/ui/StatusBadge';
import { useToast } from '@/components/ui/ToastProvider';

type Props = {
  reportId: string;
  /** Disable when the report cannot be exported (e.g. validation failing). */
  disabled?: boolean;
};

const CLEAR_MS = 30_000;

export default function CopyToRisButton({ reportId, disabled }: Props) {
  const [busy, setBusy] = useState(false);
  const [status, setStatus] = useState<'idle' | 'copied' | 'error'>('idle');
  const [error, setError] = useState<string | null>(null);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const { toast } = useToast();

  useEffect(() => () => {
    if (timerRef.current) clearTimeout(timerRef.current);
  }, []);

  useEffect(() => {
    const runCopy = () => {
      if (!disabled) void copy();
    };
    const clearStatus = () => setStatus('idle');
    window.addEventListener('radiopad:secure-copy-section', runCopy);
    window.addEventListener('radiopad:copy-to-ris', runCopy);
    window.addEventListener('radiopad:clipboard-cleared', clearStatus);
    return () => {
      window.removeEventListener('radiopad:secure-copy-section', runCopy);
      window.removeEventListener('radiopad:copy-to-ris', runCopy);
      window.removeEventListener('radiopad:clipboard-cleared', clearStatus);
    };
  }, [disabled, reportId]);

  async function copy() {
    setBusy(true);
    setError(null);
    try {
      const text = await api.reports.exportText(reportId, { preview: true });
      const invoke = getTauriInvoke();
      if (invoke) {
        try {
          await invoke('secure_copy', { text, ttlMs: CLEAR_MS });
        } catch {
          if (!navigator.clipboard?.writeText) throw new Error('Clipboard API unavailable in this context.');
          await navigator.clipboard.writeText(text);
          clearBrowserClipboardLater(text);
        }
      } else {
        if (!navigator.clipboard?.writeText) throw new Error('Clipboard API unavailable in this context.');
        await navigator.clipboard.writeText(text);
        clearBrowserClipboardLater(text);
      }
      setStatus('copied');
      toast({ tone: 'success', title: 'Copied for RIS', message: 'Clipboard auto-clears in 30s.' });
      if (timerRef.current) clearTimeout(timerRef.current);
      timerRef.current = setTimeout(() => setStatus('idle'), CLEAR_MS);
    } catch (e) {
      setStatus('error');
      const err = e as { body?: { error?: string }; message: string };
      setError(err.body?.error || err.message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <button
        className="ghost"
        onClick={copy}
        disabled={busy || disabled}
        aria-busy={busy}
        aria-label="Copy report to clipboard for RIS"
      >
        {busy && <span className="rp-spinner sm" aria-hidden />}
        {busy ? 'Copying…' : 'Copy for RIS'}
      </button>
      {status === 'copied' && (
        <span role="status">
          <StatusBadge tone="success">Copied (auto-clears in 30s)</StatusBadge>
        </span>
      )}
      {status === 'error' && error && (
        <span role="alert">
          <StatusBadge tone="danger">{error}</StatusBadge>
        </span>
      )}
    </>
  );
}

function clearBrowserClipboardLater(copiedText: string): void {
  window.setTimeout(async () => {
    try {
      if (!navigator.clipboard?.readText || !navigator.clipboard?.writeText) return;
      const current = await navigator.clipboard.readText();
      if (current !== copiedText) return;
      await navigator.clipboard.writeText('');
      window.dispatchEvent(new CustomEvent('radiopad:clipboard-cleared'));
    } catch {
      /* clipboard read may be denied; do not wipe user clipboard blindly */
    }
  }, CLEAR_MS);
}

function getTauriInvoke(): ((cmd: string, args?: unknown) => Promise<unknown>) | null {
  if (typeof window === 'undefined') return null;
  const tauri = (window as typeof window & {
    __TAURI__?: {
      core?: { invoke?: (cmd: string, args?: unknown) => Promise<unknown> };
      invoke?: (cmd: string, args?: unknown) => Promise<unknown>;
    };
  }).__TAURI__;
  return tauri?.core?.invoke ?? tauri?.invoke ?? null;
}
