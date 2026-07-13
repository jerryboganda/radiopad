'use client';

// RC-06 AI activity rail — session log of AI jobs run against this report
// (generate draft / impression, rewrites, dictation cleanup, cross-check).
// Entries are recorded by ReportClient as the existing job calls resolve, so
// the log reflects real submit+poll jobs — provider, model, prompt version and
// latency come straight from the job results. Each completed entry links to
// the RC-07 provenance modal.
import type { Provider } from '@/lib/api';
import { COMPLIANCE_LABELS } from '@/lib/api';
import EmptyState from '@/components/ui/EmptyState';
import { Sparkles } from 'lucide-react';

export interface AiActivityEntry {
  id: number;
  /** Epoch ms when the action was started. */
  startedAt: number;
  /** Human verb — "Generate Draft", "Rewrite (Concise)"… */
  action: string;
  status: 'running' | 'completed' | 'failed';
  /** Sections the action wrote into (for provenance display). */
  scope?: string;
  provider?: string;
  model?: string;
  promptVersion?: string;
  latencyMs?: number;
  error?: string;
}

export interface AiActivityPanelProps {
  entries: AiActivityEntry[];
  provider: Provider | null;
  onShowProvenance: (entry: AiActivityEntry) => void;
}

function fmtTime(epochMs: number): string {
  try {
    return new Date(epochMs).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  } catch {
    return '';
  }
}

export default function AiActivityPanel(p: AiActivityPanelProps) {
  const sorted = [...p.entries].sort((a, b) => b.startedAt - a.startedAt);
  const last = sorted[0];

  return (
    <div className="rp-aiactivity">
      <div className="rp-panel-title">Recent activity</div>
      {sorted.length === 0 ? (
        <EmptyState
          icon={<Sparkles size={18} aria-hidden />}
          title="No AI actions yet"
          description="Actions you run from the AI bar will be logged here."
        />
      ) : (
        <ul className="rp-aiactivity-list">
          {sorted.map((e) => (
            <li key={e.id} className={`rp-aiactivity-item is-${e.status}`}>
              <span className="rp-aiactivity-time">{fmtTime(e.startedAt)}</span>
              <div className="rp-aiactivity-main">
                <span className="rp-aiactivity-action">{e.action}</span>
                {(e.provider || e.model) && (
                  <span className="rp-aiactivity-route">
                    {[e.provider, e.model].filter(Boolean).join(' · ')}
                  </span>
                )}
                {e.status === 'failed' && e.error && (
                  <span className="rp-aiactivity-error">{e.error}</span>
                )}
              </div>
              {e.status === 'running' ? (
                <span className="badge info">
                  <span className="rp-spinner sm" aria-hidden /> Running
                </span>
              ) : e.status === 'failed' ? (
                <span className="badge danger">Failed</span>
              ) : (
                <button
                  type="button"
                  className="rp-aiactivity-provenance"
                  onClick={() => p.onShowProvenance(e)}
                >
                  Completed
                </button>
              )}
            </li>
          ))}
        </ul>
      )}

      {last && last.status !== 'running' && (
        <div className="rp-aiactivity-last">
          <div className="rp-panel-title">Last action</div>
          <dl className="rp-aiactivity-facts">
            <div><dt>Action</dt><dd>{last.action}</dd></div>
            <div>
              <dt>Status</dt>
              <dd>
                {last.status === 'completed'
                  ? <span className="badge ok">Completed</span>
                  : <span className="badge danger">Failed</span>}
              </dd>
            </div>
            {last.scope && <div><dt>Scope</dt><dd>{last.scope}</dd></div>}
            {typeof last.latencyMs === 'number' && (
              <div><dt>Latency</dt><dd>{(last.latencyMs / 1000).toFixed(1)}s</dd></div>
            )}
          </dl>
          <button type="button" className="rp-subtle-link" onClick={() => p.onShowProvenance(last)}>
            View provenance
          </button>
        </div>
      )}

      {p.provider && (
        <div className="rp-aiactivity-route-card">
          <div className="rp-panel-title">Route / policy</div>
          <dl className="rp-aiactivity-facts">
            <div><dt>Route</dt><dd>{p.provider.name}</dd></div>
            <div><dt>Model</dt><dd><code>{p.provider.model}</code></dd></div>
            <div>
              <dt>Policy</dt>
              <dd>
                <span className="badge info">
                  {COMPLIANCE_LABELS[p.provider.compliance] ?? `Class ${p.provider.compliance}`}
                </span>
              </dd>
            </div>
          </dl>
          <p className="rp-aiactivity-audit">All AI actions are logged and protected.</p>
        </div>
      )}
    </div>
  );
}
