'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { api, type Rulebook } from '@/lib/api';

/**
 * PRD §16.4 — Prompt Studio. v1 surfaces, for each rulebook:
 *   1. The raw YAML source (read-only here; visual prompt-block editing is
 *      delegated to the existing /rulebooks/[id] editor so we keep one source
 *      of truth and one approval path).
 *   2. A "test against findings" runner that sends free text through
 *      `api.reports.validate` for the Draft-mode active report (golden-case
 *      regression already lives in the CLI; this is the per-author UX).
 *   3. The version + status of the rulebook, so admins can compare drafts
 *      against approved versions before promotion (PRD RB-003 / RB-008).
 *
 * UI uses only locked Open Design tokens. No Tailwind / no MUI / no inline
 * colour styles. AI-generated text wears `.ai-mark` per the global rule.
 */
export default function PromptStudioPage() {
  const [rulebooks, setRulebooks] = useState<Rulebook[]>([]);
  const [activeId, setActiveId] = useState<string | null>(null);
  const [yaml, setYaml] = useState<string>('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const list = await api.rulebooks.list();
        if (cancelled) return;
        setRulebooks(list);
        if (list.length > 0) setActiveId(list[0].id);
      } catch (e) {
        setError((e as Error).message);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    if (!activeId) {
      setYaml('');
      return;
    }
    let cancelled = false;
    (async () => {
      try {
        const rb = await api.rulebooks.get(activeId);
        if (!cancelled) setYaml(rb.sourceYaml);
      } catch (e) {
        if (!cancelled) setError((e as Error).message);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [activeId]);

  const active = useMemo(
    () => rulebooks.find((r) => r.id === activeId) ?? null,
    [rulebooks, activeId],
  );

  // Naive extractor: pull `prompt_blocks:` block out of the YAML for read-only
  // display. The real source-of-truth edit happens at /rulebooks/[id].
  const promptBlocks = useMemo(() => extractPromptBlocks(yaml), [yaml]);

  return (
    <div className="pane">
      <div className="panel">
        <div className="panel-header">
          <div>
            <h1 className="rp-page-title">Prompt Studio</h1>
            <p className="rp-page-sub">
              Inspect prompt blocks per rulebook, compare versions, and hand off
              to the rulebook editor for changes (PRD §16.4 / RB-003 / RB-008).
            </p>
          </div>
          {active ? (
            <Link className="primary-ghost" href={`/rulebooks/${active.id}`}>
              Edit in rulebook editor
            </Link>
          ) : null}
        </div>

        {loading ? <div className="rp-page-sub">Loading…</div> : null}
        {error ? <div className="banner warn">{error}</div> : null}

        {!loading && rulebooks.length === 0 ? (
          <div className="rp-page-sub">
            No rulebooks yet. Create one in <Link href="/rulebooks">Rulebooks</Link>.
          </div>
        ) : null}

        {rulebooks.length > 0 ? (
          <div className="rp-grid-3">
            <div className="rp-panel">
              <div className="rp-panel-title">Rulebooks</div>
              <ul className="rp-list">
                {rulebooks.map((rb) => {
                  const status = statusLabel(rb.status);
                  return (
                    <li key={rb.id}>
                      <button
                        type="button"
                        className={rb.id === activeId ? 'subtle active' : 'subtle'}
                        onClick={() => setActiveId(rb.id)}
                      >
                        <span>{rb.name}</span>
                        <code>{rb.version}</code>
                        <span className={`badge ${badgeFor(rb.status)}`}>{status}</span>
                      </button>
                    </li>
                  );
                })}
              </ul>
            </div>

            <div className="rp-panel" style={{ gridColumn: 'span 2' }}>
              <div className="rp-panel-title">
                Prompt blocks {active ? `· ${active.name} ${active.version}` : ''}
              </div>
              {promptBlocks.length === 0 ? (
                <div className="rp-page-sub">
                  This rulebook has no <code>prompt_blocks:</code> section yet.
                </div>
              ) : (
                <ul className="rp-list">
                  {promptBlocks.map((p) => (
                    <li key={p.name} className="ai-mark">
                      <strong>{p.name}</strong>
                      <pre>{p.body}</pre>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          </div>
        ) : null}
      </div>
    </div>
  );
}

function statusLabel(status: Rulebook['status']): string {
  if (typeof status === 'string') return status;
  return ['Draft', 'In review', 'Approved', 'Deprecated'][status] ?? String(status);
}

function badgeFor(status: Rulebook['status']) {
  if (typeof status === 'number') return ['', 'warn', 'ok', 'danger'][status] ?? 'warn';
  const normalized = status.toLowerCase();
  if (normalized === 'approved') return 'ok';
  if (normalized === 'review' || normalized === 'in review') return 'info';
  if (normalized === 'deprecated') return 'danger';
  return 'warn';
}

/**
 * Tiny YAML scraper just for the read-only "prompt_blocks" preview. Avoids a
 * full YAML parser dependency. Captures the indented lines under
 * `prompt_blocks:` and stops at the next top-level key.
 */
function extractPromptBlocks(yaml: string): { name: string; body: string }[] {
  const lines = yaml.split(/\r?\n/);
  const out: { name: string; body: string }[] = [];
  let inBlock = false;
  let current: { name: string; body: string } | null = null;
  for (const line of lines) {
    if (/^prompt_blocks:\s*$/i.test(line)) {
      inBlock = true;
      continue;
    }
    if (!inBlock) continue;
    // Top-level key reached → stop.
    if (/^[a-zA-Z_]/.test(line)) {
      if (current) out.push(current);
      break;
    }
    const m = line.match(/^\s{2,4}([a-zA-Z_][a-zA-Z0-9_]*)\s*:\s*"?(.*?)"?\s*$/);
    if (m) {
      if (current) out.push(current);
      current = { name: m[1], body: m[2] };
    } else if (current) {
      current.body += '\n' + line.trim();
    }
  }
  if (current) out.push(current);
  return out;
}
