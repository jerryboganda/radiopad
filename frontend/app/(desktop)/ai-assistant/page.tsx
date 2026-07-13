'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import {
  ArrowRight,
  BookOpenCheck,
  FileText,
  Mic,
  RefreshCw,
  ShieldCheck,
  Sparkles,
  Wand2,
} from 'lucide-react';
import { api, COMPLIANCE_LABELS, type Provider } from '@/lib/api';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import { TableSkeleton } from '@/components/ui/Skeleton';
import EmptyState from '@/components/ui/EmptyState';
import ErrorState from '@/components/ui/ErrorState';
import { STT_MODES, useSttMode } from '@/lib/dictation/sttMode';
import { useCrossCheckEnabled, useUseUbag } from '@/lib/dictation/crossCheckPrefs';
import type { SttMode } from '@/lib/api';

/**
 * AI Assistant hub — one place to see the models the workspace can use,
 * what the AI has been doing lately, and the personal dictation /
 * cross-check preferences (same controls as the profile menu).
 */
export default function AiAssistantPage() {
  return (
    <Container>
      <PageHeader
        title="AI Assistant"
        description="Your AI models, recent AI activity, and dictation preferences — all in one place."
      />
      <ProvidersPanel />
      {/* Full-width stacked panels — a 50/50 split cramped the activity table and
          the dictation controls; each panel is a self-spacing .rp-panel. */}
      <RecentActivityPanel />
      <DictationSettingsPanel />
      <QuickLinksPanel />
    </Container>
  );
}

/* ── (a) AI providers ─────────────────────────────────────────────────── */

// Same tone mapping as the admin models page: compliance class → badge tone.
const COMPLIANCE_BADGE: Record<number, string> = {
  0: 'danger', // Blocked
  1: 'warn',   // Sandbox
  2: 'info',   // De-identified only
  3: 'ok',     // PHI-approved
  4: 'ai',     // Local-only
};

type HealthResult = 'checking' | 'healthy' | 'down';

function statusFromError(e: unknown): number | null {
  const s = (e as { status?: unknown }).status;
  return typeof s === 'number' ? s : null;
}

function ProvidersPanel() {
  const [providers, setProviders] = useState<Provider[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [noAccess, setNoAccess] = useState(false);
  const [health, setHealth] = useState<Record<string, { state: HealthResult; note?: string }>>({});

  const probe = useCallback((rows: Provider[]) => {
    const enabled = rows.filter((p) => p.enabled);
    setHealth((prev) => {
      const next = { ...prev };
      for (const p of enabled) next[p.id] = { state: 'checking' };
      return next;
    });
    for (const p of enabled) {
      api.providers
        .health(p.id)
        .then((r) =>
          setHealth((m) => ({
            ...m,
            [p.id]: r.ok
              ? { state: 'healthy', note: r.note ?? undefined }
              : { state: 'down', note: r.error ?? r.note ?? 'not reachable' },
          })),
        )
        .catch((e: Error) =>
          setHealth((m) => ({ ...m, [p.id]: { state: 'down', note: e.message } })),
        );
    }
  }, []);

  const load = useCallback(() => {
    setLoading(true);
    setError(null);
    setNoAccess(false);
    api.providers
      .list()
      .then((rows) => {
        setProviders(rows);
        probe(rows);
      })
      .catch((e: Error) => {
        if (statusFromError(e) === 403) setNoAccess(true);
        else setError(e.message);
      })
      .finally(() => setLoading(false));
  }, [probe]);

  useEffect(() => {
    load();
  }, [load]);

  return (
    <div className="rp-panel rp-anim-fade-in-up" aria-live="polite" aria-busy={loading}>
      <div className="rp-row" style={{ justifyContent: 'space-between', alignItems: 'baseline' }}>
        <div className="rp-panel-title">AI providers</div>
        {!loading && !noAccess && providers.length > 0 && (
          <button className="subtle" onClick={() => probe(providers)}>
            <RefreshCw size={13} strokeWidth={1.8} aria-hidden /> Re-check health
          </button>
        )}
      </div>

      {loading ? (
        <TableSkeleton rows={3} cols={3} />
      ) : noAccess ? (
        <EmptyState
          icon={<ShieldCheck size={18} strokeWidth={1.6} aria-hidden />}
          title="Provider details are managed by your workspace admin"
          description="You don't have permission to view the model list here. The AI features on your reports keep working as usual."
        />
      ) : error ? (
        <ErrorState title="Couldn't load AI providers" message={error} onRetry={load} />
      ) : providers.length === 0 ? (
        <EmptyState
          title="No AI models configured yet"
          description="Ask a workspace administrator to add a model before using AI drafting."
        />
      ) : (
        <div className="rp-card-grid">
          {providers.map((p) => {
            const h = p.enabled ? health[p.id] : undefined;
            return (
              <div key={p.id} className="rp-card">
                <div className="rp-card-head">
                  <p className="rp-card-title">{p.name}</p>
                  <HealthBadge enabled={p.enabled} health={h} />
                </div>
                <p className="rp-card-meta">
                  {p.adapter}
                  {p.model ? ` · ${p.model}` : ''}
                </p>
                <div className="rp-row" style={{ gap: 6, flexWrap: 'wrap' }}>
                  <span className={`badge ${COMPLIANCE_BADGE[p.compliance] ?? ''}`}>
                    {COMPLIANCE_LABELS[p.compliance] ?? 'Unknown'}
                  </span>
                  {p.retentionLabel && <span className="badge">{p.retentionLabel}</span>}
                </div>
                {h?.state === 'down' && h.note && (
                  <p className="rp-card-meta" style={{ marginTop: 4 }}>{h.note}</p>
                )}
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}

function HealthBadge({
  enabled,
  health,
}: {
  enabled: boolean;
  health?: { state: HealthResult; note?: string };
}) {
  if (!enabled) return <span className="status-badge" data-tone="muted">Off</span>;
  if (!health || health.state === 'checking')
    return <span className="status-badge" data-tone="review">Checking</span>;
  if (health.state === 'healthy')
    return <span className="status-badge" data-tone="ready">Healthy</span>;
  return <span className="status-badge" data-tone="blocked">Unreachable</span>;
}

/* ── (b) Recent AI activity ───────────────────────────────────────────── */

type AuditEvent = {
  id: string;
  userId: string | null;
  reportId: string | null;
  action: number | string;
  detailsJson: string;
  createdAt: string;
};

// Numeric action codes that are AI-related (see the Activity log page map).
const AI_ACTION_LABEL: Record<number, string> = {
  0: 'AI request',
  1: 'AI response',
  5: 'Provider blocked',
  9: 'Policy violation',
  54: 'Provider configured',
};

const AI_ACTION_TONE: Record<number, string> = {
  0: 'info',
  1: 'ai',
  5: 'danger',
  9: 'warn',
  54: 'info',
};

const AI_STRING_TOKENS = ['airequest', 'airesponse', 'providerblocked', 'providerconfigured', 'policyviolation', 'sandbox', 'prompt'];

function isAiEvent(e: AuditEvent): boolean {
  if (typeof e.action === 'number') return e.action in AI_ACTION_LABEL;
  const a = String(e.action).toLowerCase();
  return AI_STRING_TOKENS.some((t) => a.includes(t));
}

function actionLabel(action: number | string): string {
  if (typeof action === 'number') return AI_ACTION_LABEL[action] ?? `Action ${action}`;
  return String(action);
}

function actionTone(action: number | string): string {
  if (typeof action === 'number') return AI_ACTION_TONE[action] ?? '';
  const a = String(action).toLowerCase();
  if (a.includes('blocked') || a.includes('violation')) return 'danger';
  if (a.includes('response')) return 'ai';
  return 'info';
}

function detailsSnippet(json: string): string {
  if (!json) return '';
  const flat = json.replace(/\s+/g, ' ').trim();
  return flat.length > 90 ? `${flat.slice(0, 90)}…` : flat;
}

function RecentActivityPanel() {
  const [events, setEvents] = useState<AuditEvent[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [noAccess, setNoAccess] = useState(false);

  const load = useCallback(() => {
    setLoading(true);
    setError(null);
    setNoAccess(false);
    api.audit
      .query({ take: 200 })
      .then((rows) => setEvents((rows as AuditEvent[]).filter(isAiEvent).slice(0, 15)))
      .catch((e: Error) => {
        if (statusFromError(e) === 403) setNoAccess(true);
        else setError(e.message);
      })
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  return (
    <div className="rp-panel rp-anim-fade-in-up" aria-live="polite" aria-busy={loading}>
      <div className="rp-row" style={{ justifyContent: 'space-between', alignItems: 'baseline' }}>
        <div className="rp-panel-title">Recent AI activity</div>
        <Link className="subtle" href="/audit" style={{ textDecoration: 'none' }}>
          Full activity log
        </Link>
      </div>

      {loading ? (
        <TableSkeleton rows={5} cols={3} />
      ) : noAccess ? (
        <EmptyState
          icon={<ShieldCheck size={18} strokeWidth={1.6} aria-hidden />}
          title="Activity log is restricted"
          description="You don't have permission to view workspace activity. Ask an administrator if you need access."
        />
      ) : error ? (
        <ErrorState title="Couldn't load AI activity" message={error} onRetry={load} />
      ) : events.length === 0 ? (
        <EmptyState
          icon={<Sparkles size={18} strokeWidth={1.6} aria-hidden />}
          title="No AI activity yet"
          description="AI requests, responses, and policy decisions will show up here as you work."
        />
      ) : (
        <div className="table-wrap">
          <table className="rp-table">
            <thead>
              <tr>
                <th>When</th>
                <th>Event</th>
                <th>Details</th>
              </tr>
            </thead>
            <tbody>
              {events.map((e) => (
                <tr key={e.id}>
                  <td style={{ whiteSpace: 'nowrap' }}>{new Date(e.createdAt).toLocaleString()}</td>
                  <td>
                    <span className={`badge ${actionTone(e.action)}`}>{actionLabel(e.action)}</span>
                  </td>
                  <td className="rp-faint">{detailsSnippet(e.detailsJson) || '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

/* ── (c) Dictation & cross-check ──────────────────────────────────────── */

const STT_MODE_LABEL: Record<SttMode, string> = {
  auto: 'Auto (recommended)',
  single: 'Single engine',
  ensemble: 'Dual engine',
};

function SettingRow({
  label,
  description,
  control,
}: {
  label: string;
  description: string;
  control: React.ReactNode;
}) {
  return (
    <div
      className="rp-row"
      style={{
        justifyContent: 'space-between',
        alignItems: 'flex-start',
        gap: 16,
        padding: '12px 0',
        borderTop: '1px solid var(--border-soft)',
      }}
    >
      <div style={{ minWidth: 0 }}>
        <div className="text-ink" style={{ fontWeight: 600, fontSize: 14 }}>{label}</div>
        <p className="rp-page-sub" style={{ margin: '2px 0 0' }}>{description}</p>
      </div>
      <div style={{ flexShrink: 0 }}>{control}</div>
    </div>
  );
}

function DictationSettingsPanel() {
  const [mode, setMode] = useSttMode();
  const [ccEnabled, setCcEnabled] = useCrossCheckEnabled();
  const [ccUbag, setCcUbag] = useUseUbag();

  return (
    <div className="rp-panel rp-anim-fade-in-up">
      <div className="rp-row" style={{ gap: 8, alignItems: 'baseline' }}>
        <Mic size={15} strokeWidth={1.8} aria-hidden />
        <div className="rp-panel-title" style={{ marginBottom: 0 }}>Dictation &amp; cross-check</div>
      </div>
      <p className="rp-page-sub" style={{ marginTop: 4 }}>
        These are your personal preferences — the same ones in the profile menu.
      </p>

      <SettingRow
        label="Speech recognition mode"
        description="Auto picks the best on-device setup for your machine. Dual engine runs two engines at once for extra accuracy (uses more CPU and RAM)."
        control={
          <div className="rp-row" style={{ gap: 6, flexWrap: 'wrap' }}>
            {STT_MODES.map((m) => (
              <button
                key={m}
                className={`ghost${mode === m ? ' active' : ''}`}
                onClick={() => setMode(m)}
                aria-pressed={mode === m}
              >
                {STT_MODE_LABEL[m]}
              </button>
            ))}
          </div>
        }
      />

      <SettingRow
        label="Dual-engine cross-check"
        description="Cross-check each dictation with a second on-device engine and flag disagreements for review."
        control={
          <label className="rp-row" style={{ gap: 8, cursor: 'pointer' }}>
            <input
              type="checkbox"
              checked={mode === 'ensemble'}
              onChange={(e) => setMode(e.target.checked ? 'ensemble' : 'single')}
            />
            <span className="rp-page-sub">{mode === 'ensemble' ? 'On' : 'Off'}</span>
          </label>
        }
      />

      <SettingRow
        label="Manual Cross Check"
        description="Show a Cross Check button that re-runs a dictation through extra engines plus a medical-AI review, highlighting suggested corrections."
        control={
          <label className="rp-row" style={{ gap: 8, cursor: 'pointer' }}>
            <input
              type="checkbox"
              checked={ccEnabled}
              onChange={(e) => setCcEnabled(e.target.checked)}
            />
            <span className="rp-page-sub">{ccEnabled ? 'On' : 'Off'}</span>
          </label>
        }
      />

      <SettingRow
        label="Cross Check via UBAG (no PHI)"
        description="Also route the medical-accuracy review through the UBAG cloud AI. Only use on text with no patient-identifying information."
        control={
          <label className="rp-row" style={{ gap: 8, cursor: ccEnabled ? 'pointer' : 'not-allowed' }}>
            <input
              type="checkbox"
              checked={ccUbag}
              disabled={!ccEnabled}
              onChange={(e) => setCcUbag(e.target.checked)}
            />
            <span className="rp-page-sub">{ccUbag ? 'On' : 'Off'}</span>
          </label>
        }
      />
    </div>
  );
}

/* ── (d) Quick links ──────────────────────────────────────────────────── */

const QUICK_LINKS = [
  {
    href: '/prompts',
    title: 'Prompt studio',
    description: 'Tune and test the prompts behind AI drafting.',
    icon: Wand2,
  },
  {
    href: '/templates',
    title: 'Templates',
    description: 'Report layouts the AI fills in for each study type.',
    icon: FileText,
  },
  {
    href: '/rulebooks',
    title: 'Rulebooks',
    description: 'The checks every AI draft is validated against.',
    icon: BookOpenCheck,
  },
] as const;

function QuickLinksPanel() {
  return (
    <div className="rp-panel rp-anim-fade-in-up">
      <div className="rp-panel-title">Build with AI</div>
      <div className="rp-card-grid">
        {QUICK_LINKS.map(({ href, title, description, icon: Icon }) => (
          <Link key={href} href={href} className="rp-card" style={{ textDecoration: 'none' }}>
            <div className="rp-card-head">
              <p className="rp-card-title rp-row" style={{ gap: 8 }}>
                <Icon size={15} strokeWidth={1.8} aria-hidden />
                {title}
              </p>
              <ArrowRight size={15} strokeWidth={1.8} aria-hidden className="text-ink-soft" />
            </div>
            <p className="rp-card-meta">{description}</p>
          </Link>
        ))}
      </div>
    </div>
  );
}
