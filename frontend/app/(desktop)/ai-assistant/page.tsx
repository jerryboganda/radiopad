'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import {
  ArrowRight,
  BookOpenCheck,
  Bot,
  Check,
  CheckCircle2,
  Clock,
  Cloud,
  Cpu,
  FileText,
  MoreHorizontal,
  RefreshCw,
  Settings2,
  ShieldCheck,
  Sparkles,
  TrendingUp,
  Users,
  Wand2,
  XCircle,
} from 'lucide-react';
import { api, COMPLIANCE_LABELS, type Provider } from '@/lib/api';
import Container from '@/components/shell/Container';
import { TableSkeleton } from '@/components/ui/Skeleton';
import EmptyState from '@/components/ui/EmptyState';
import ErrorState from '@/components/ui/ErrorState';
import { STT_MODES, useSttMode } from '@/lib/dictation/sttMode';
import { usePreferredProviderId } from '@/lib/ai/providerPref';
import { useCrossCheckEnabled, useUseUbag } from '@/lib/dictation/crossCheckPrefs';
import type { SttMode } from '@/lib/api';

/**
 * AI Assistant hub — the models the workspace can use, what the AI has been
 * doing lately, and the personal dictation / cross-check preferences (the same
 * controls as the profile menu), with an at-a-glance summary on top.
 */

// Same tone mapping as the admin models page: compliance class → badge tone.
const COMPLIANCE_BADGE: Record<number, string> = {
  0: 'danger', // Blocked
  1: 'warn',   // Sandbox
  2: 'info',   // De-identified only
  3: 'ok',     // PHI-approved
  4: 'ai',     // Local-only
};

type HealthResult = 'checking' | 'healthy' | 'down';
type Health = Record<string, { state: HealthResult; note?: string }>;

function statusFromError(e: unknown): number | null {
  const s = (e as { status?: unknown }).status;
  return typeof s === 'number' ? s : null;
}

/** Coarse provider family, used only to pick the card's icon and tint. */
function providerKind(adapter: string): 'local' | 'ubag' | 'cloud' {
  const a = adapter.toLowerCase();
  if (a.includes('ubag')) return 'ubag';
  if (a.includes('llama') || a.includes('local') || a.includes('onnx') || a.includes('gguf'))
    return 'local';
  return 'cloud';
}

/**
 * A neutral glyph per provider family. Deliberately not vendor logos — shipping
 * third-party marks in the bundle is a licensing question we do not need to open.
 */
function providerIcon(adapter: string) {
  const a = adapter.toLowerCase();
  if (providerKind(adapter) === 'local') return Cpu;
  if (a.includes('gemini')) return Sparkles;
  if (a.includes('chatgpt') || a.includes('openai') || a.includes('gpt')) return Bot;
  if (a.includes('deepseek')) return Cloud;
  return Cloud;
}

export default function AiAssistantPage() {
  const [providers, setProviders] = useState<Provider[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [noAccess, setNoAccess] = useState(false);
  const [health, setHealth] = useState<Health>({});
  // The radiologist's own default engine — personal, not an admin setting.
  const [preferredId, setPreferredId] = usePreferredProviderId();

  const [events, setEvents] = useState<AuditEvent[]>([]);
  const [eventsLoading, setEventsLoading] = useState(true);
  const [eventsError, setEventsError] = useState<string | null>(null);
  const [eventsNoAccess, setEventsNoAccess] = useState(false);

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

  const loadProviders = useCallback(() => {
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

  const loadEvents = useCallback(() => {
    setEventsLoading(true);
    setEventsError(null);
    setEventsNoAccess(false);
    api.audit
      .query({ take: 200 })
      .then((rows) => setEvents((rows as AuditEvent[]).filter(isAiEvent)))
      .catch((e: Error) => {
        if (statusFromError(e) === 403) setEventsNoAccess(true);
        else setEventsError(e.message);
      })
      .finally(() => setEventsLoading(false));
  }, []);

  useEffect(() => {
    loadProviders();
    loadEvents();
  }, [loadProviders, loadEvents]);

  const reachableCount = useMemo(
    () => providers.filter((p) => p.enabled && health[p.id]?.state === 'healthy').length,
    [providers, health],
  );

  const todayCount = useMemo(() => {
    const today = new Date().toDateString();
    return events.filter((e) => new Date(e.createdAt).toDateString() === today).length;
  }, [events]);

  const defaultCount = preferredId && providers.some((p) => p.id === preferredId) ? 1 : 0;

  return (
    <Container>
      <div className="rp-ai-hero">
        <div className="rp-ai-hero-main">
          <div className="rp-ai-hero-icon" aria-hidden>
            <Sparkles size={26} strokeWidth={1.6} />
          </div>
          <div className="rp-ai-hero-text">
            <h1 className="rp-page-title">AI Assistant</h1>
            <p className="rp-page-sub">
              Your AI models, recent activity, and dictation preferences — all in one place.
            </p>
          </div>
        </div>
        <div className="rp-ai-hero-actions">
          <button
            type="button"
            className="ghost"
            onClick={() => {
              probe(providers);
              loadEvents();
            }}
            disabled={loading || providers.length === 0}
          >
            <RefreshCw size={14} strokeWidth={1.8} aria-hidden /> Re-check health
          </button>
        </div>
      </div>

      <div className="rp-ai-stats">
        <StatCard kind="providers" icon={Users} value={providers.length} label="Providers" />
        <StatCard kind="default" icon={CheckCircle2} value={defaultCount} label="Default engine" />
        <StatCard kind="reachable" icon={Cloud} value={reachableCount} label="Reachable" />
        <StatCard
          kind="activity"
          icon={TrendingUp}
          value={todayCount}
          label="Recent activity today"
        />
      </div>

      <ProvidersPanel
        providers={providers}
        health={health}
        loading={loading}
        error={error}
        noAccess={noAccess}
        preferredId={preferredId}
        onSetPreferred={setPreferredId}
        onRetry={loadProviders}
        onProbeOne={(p) => probe([p])}
      />

      <div className="rp-ai-cols">
        <RecentActivityPanel
          events={events}
          loading={eventsLoading}
          error={eventsError}
          noAccess={eventsNoAccess}
          onRetry={loadEvents}
        />
        <DictationSettingsPanel />
      </div>

      <QuickLinksPanel />
    </Container>
  );
}

function StatCard({
  kind,
  icon: Icon,
  value,
  label,
}: {
  kind: string;
  icon: typeof Users;
  value: number;
  label: string;
}) {
  return (
    <div className="rp-ai-stat">
      <div className={`rp-ai-stat-icon ${kind}`} aria-hidden>
        <Icon size={19} strokeWidth={1.8} />
      </div>
      <div className="rp-ai-stat-body">
        <div className="rp-ai-stat-value">{value}</div>
        <div className="rp-ai-stat-label">{label}</div>
      </div>
    </div>
  );
}

/* ── (a) AI providers ─────────────────────────────────────────────────── */

/** How many provider cards the strip shows before "View all providers". */
const PROVIDER_PREVIEW = 5;

function ProvidersPanel({
  providers,
  health,
  loading,
  error,
  noAccess,
  preferredId,
  onSetPreferred,
  onRetry,
  onProbeOne,
}: {
  providers: Provider[];
  health: Health;
  loading: boolean;
  error: string | null;
  noAccess: boolean;
  preferredId: string;
  onSetPreferred: (id: string) => void;
  onRetry: () => void;
  onProbeOne: (p: Provider) => void;
}) {
  const [showAll, setShowAll] = useState(false);
  const visible = showAll ? providers : providers.slice(0, PROVIDER_PREVIEW);

  return (
    <div className="rp-panel rp-anim-fade-in-up" aria-live="polite" aria-busy={loading}>
      <div className="rp-ai-panel-head">
        <div className="rp-panel-title">AI providers</div>
        {!loading && !noAccess && providers.length > PROVIDER_PREVIEW && (
          <button type="button" className="rp-ai-link" onClick={() => setShowAll((v) => !v)}>
            {showAll ? 'Show fewer' : `View all providers (${providers.length})`}
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
        <ErrorState title="Couldn't load AI providers" message={error} onRetry={onRetry} />
      ) : providers.length === 0 ? (
        <EmptyState
          title="No AI models configured yet"
          description="Ask a workspace administrator to add a model before using AI drafting."
        />
      ) : (
        <div className="rp-ai-providers">
          {visible.map((p) => (
            <ProviderCard
              key={p.id}
              provider={p}
              health={p.enabled ? health[p.id] : undefined}
              isDefault={preferredId === p.id}
              onSetPreferred={onSetPreferred}
              onProbe={() => onProbeOne(p)}
            />
          ))}
        </div>
      )}
    </div>
  );
}

function ProviderCard({
  provider: p,
  health: h,
  isDefault,
  onSetPreferred,
  onProbe,
}: {
  provider: Provider;
  health?: { state: HealthResult; note?: string };
  isDefault: boolean;
  onSetPreferred: (id: string) => void;
  onProbe: () => void;
}) {
  const [menuOpen, setMenuOpen] = useState(false);
  const Icon = providerIcon(p.adapter);
  const kind = providerKind(p.adapter);

  return (
    <div className={`rp-ai-provider${isDefault ? ' is-default' : ''}`}>
      <div className="rp-ai-provider-head">
        <div className="rp-ai-provider-icon" data-kind={kind} aria-hidden>
          <Icon size={16} strokeWidth={1.8} />
        </div>
        <div style={{ minWidth: 0 }}>
          <p className="rp-ai-provider-name">{p.name}</p>
          <div className="rp-ai-provider-sub">
            {p.adapter}
            {p.model ? ` · ${p.model}` : ''}
          </div>
        </div>
        <div className="rp-ai-provider-menu">
          <button
            type="button"
            className="ghost"
            onClick={() => setMenuOpen((v) => !v)}
            aria-expanded={menuOpen}
            aria-label={`More actions for ${p.name}`}
          >
            <MoreHorizontal size={15} strokeWidth={1.8} aria-hidden />
          </button>
          {menuOpen && (
            <div className="rp-ai-provider-menu-list" role="menu">
              <button
                type="button"
                role="menuitem"
                onClick={() => {
                  setMenuOpen(false);
                  onProbe();
                }}
                disabled={!p.enabled}
              >
                Re-check this provider
              </button>
              {isDefault && (
                <button
                  type="button"
                  role="menuitem"
                  onClick={() => {
                    setMenuOpen(false);
                    onSetPreferred('');
                  }}
                >
                  Clear my default
                </button>
              )}
            </div>
          )}
        </div>
      </div>

      <div className="rp-row" style={{ gap: 6, flexWrap: 'wrap' }}>
        <HealthBadge enabled={p.enabled} health={h} />
        <span className={`badge ${COMPLIANCE_BADGE[p.compliance] ?? ''}`}>
          {COMPLIANCE_LABELS[p.compliance] ?? 'Unknown'}
        </span>
        {p.retentionLabel && <span className="badge">{p.retentionLabel}</span>}
      </div>

      {h?.state === 'down' && h.note && <p className="rp-ai-provider-note">{h.note}</p>}

      {p.enabled &&
        (isDefault ? (
          <button
            type="button"
            className="primary-ghost rp-ai-provider-btn"
            aria-pressed="true"
            title="This is your default engine. Click to clear and fall back to the workspace default."
            onClick={() => onSetPreferred('')}
          >
            <Check size={14} strokeWidth={2} aria-hidden /> My default engine
          </button>
        ) : (
          <button
            type="button"
            className="ghost rp-ai-provider-btn"
            aria-pressed="false"
            title="New reports will start on this engine (you can still switch per report)"
            onClick={() => onSetPreferred(p.id)}
          >
            Set as default
          </button>
        ))}
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
  if (!enabled) return <span className="badge">Off</span>;
  if (!health || health.state === 'checking') return <span className="badge warn">Checking</span>;
  if (health.state === 'healthy') return <span className="badge ok">Reachable</span>;
  return <span className="badge danger">Unreachable</span>;
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

/**
 * The subset of `detailsJson` keys the AI paths actually write (AiGateway,
 * ReportsLifecycleController rewrite, UbagController). Everything the activity
 * table shows comes from here — nothing is invented.
 */
type AiDetails = {
  provider?: string;
  adapter?: string;
  model?: string;
  kind?: string;
  eventType?: string;
  target?: string;
  mode?: string;
  sections?: string[];
  status?: string;
  reason?: string;
  message?: string;
  error?: string;
  latencyMs?: number;
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

const ACTIVITY_LIMIT = 8;

function isAiEvent(e: AuditEvent): boolean {
  if (typeof e.action === 'number') return e.action in AI_ACTION_LABEL;
  const a = String(e.action).toLowerCase();
  return AI_STRING_TOKENS.some((t) => a.includes(t));
}

function parseDetails(json: string): AiDetails {
  if (!json) return {};
  try {
    const parsed = JSON.parse(json) as unknown;
    return parsed && typeof parsed === 'object' ? (parsed as AiDetails) : {};
  } catch {
    return {};
  }
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

/**
 * The event chip. A rewrite and a cross-check are both audited as generic
 * AI request/response rows, so the specific kind has to come out of the
 * details payload — `kind` for rewrites, `eventType` for UBAG cross-checks.
 */
function eventLabel(e: AuditEvent, d: AiDetails): string {
  if (d.kind === 'rewrite') return 'Rewrite';
  const et = (d.eventType ?? '').toLowerCase();
  if (et.includes('cross')) return 'Cross-check';
  if (et.includes('compare')) return 'Compare';
  return actionLabel(e.action);
}

function eventTone(e: AuditEvent, d: AiDetails): string {
  if (d.kind === 'rewrite') return 'warn';
  if ((d.eventType ?? '').toLowerCase().includes('cross')) return 'ai';
  return actionTone(e.action);
}

/** A one-line human description built only from keys the audit row actually has. */
function detailsText(d: AiDetails): string {
  if (d.reason) return d.reason;
  if (d.message) return d.message;
  if (d.kind === 'rewrite') {
    const mode = d.mode ? `${d.mode} rewrite` : 'Section rewrite';
    return d.sections?.length ? `${mode} · ${d.sections.join(', ')}` : mode;
  }
  if (d.eventType) return d.target ? `${d.eventType} · ${d.target}` : d.eventType;
  const bits: string[] = [];
  if (d.model) bits.push(d.model);
  if (typeof d.latencyMs === 'number') bits.push(`${d.latencyMs} ms`);
  return bits.join(' · ');
}

/**
 * Outcome for the Status column. UBAG rows carry an explicit `status`;
 * everything else is inferred from the audit action, and an AI *request* is
 * reported as "Sent" rather than "Success" — the request row is written before
 * the call returns, so it does not know the outcome.
 */
function outcome(e: AuditEvent, d: AiDetails): { label: string; tone: 'ok' | 'warn' | 'bad' | 'muted' } {
  if (d.status) {
    const s = d.status.toLowerCase();
    if (s === 'ok' || s === 'success' || s === 'completed') return { label: 'Success', tone: 'ok' };
    if (s === 'blocked') return { label: 'Blocked', tone: 'warn' };
    if (s === 'error' || s === 'failed') return { label: 'Failed', tone: 'bad' };
    return { label: d.status, tone: 'muted' };
  }
  const a = typeof e.action === 'number' ? e.action : -1;
  if (a === 1) return { label: 'Success', tone: 'ok' };
  if (a === 5) return { label: 'Blocked', tone: 'warn' };
  if (a === 9) return { label: 'Failed', tone: 'bad' };
  if (a === 0) return { label: 'Sent', tone: 'muted' };
  return { label: '—', tone: 'muted' };
}

function RecentActivityPanel({
  events,
  loading,
  error,
  noAccess,
  onRetry,
}: {
  events: AuditEvent[];
  loading: boolean;
  error: string | null;
  noAccess: boolean;
  onRetry: () => void;
}) {
  const rows = events.slice(0, ACTIVITY_LIMIT);

  return (
    <div className="rp-panel rp-anim-fade-in-up" aria-live="polite" aria-busy={loading}>
      <div className="rp-ai-panel-head">
        <div className="rp-ai-panel-title-row">
          <Clock size={15} strokeWidth={1.8} aria-hidden />
          <div className="rp-panel-title">Recent AI activity</div>
        </div>
        <Link className="rp-ai-link" href="/audit">
          View full activity log
        </Link>
      </div>

      {loading ? (
        <TableSkeleton rows={5} cols={4} />
      ) : noAccess ? (
        <EmptyState
          icon={<ShieldCheck size={18} strokeWidth={1.6} aria-hidden />}
          title="Activity log is restricted"
          description="You don't have permission to view workspace activity. Ask an administrator if you need access."
        />
      ) : error ? (
        <ErrorState title="Couldn't load AI activity" message={error} onRetry={onRetry} />
      ) : rows.length === 0 ? (
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
                <th>Time</th>
                <th>Event</th>
                <th>Provider</th>
                <th>Action / Details</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((e) => {
                const d = parseDetails(e.detailsJson);
                const o = outcome(e, d);
                return (
                  <tr key={e.id}>
                    <td style={{ whiteSpace: 'nowrap' }}>
                      {new Date(e.createdAt).toLocaleString()}
                    </td>
                    <td>
                      <span className={`badge ${eventTone(e, d)}`}>{eventLabel(e, d)}</span>
                    </td>
                    <td style={{ whiteSpace: 'nowrap' }}>{d.provider ?? '—'}</td>
                    <td className="rp-faint">{detailsText(d) || '—'}</td>
                    <td>
                      <span className="rp-ai-activity-status" data-tone={o.tone}>
                        {o.tone === 'ok' ? (
                          <CheckCircle2 size={13} strokeWidth={2} aria-hidden />
                        ) : o.tone === 'bad' ? (
                          <XCircle size={13} strokeWidth={2} aria-hidden />
                        ) : null}
                        {o.label}
                      </span>
                    </td>
                  </tr>
                );
              })}
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
    <div className="rp-ai-setting">
      <div className="rp-ai-setting-text">
        <div className="rp-ai-setting-label">{label}</div>
        <p className="rp-ai-setting-desc">{description}</p>
      </div>
      <div className="rp-ai-setting-control">{control}</div>
    </div>
  );
}

function Switch({
  checked,
  disabled,
  onChange,
  label,
}: {
  checked: boolean;
  disabled?: boolean;
  onChange: (next: boolean) => void;
  label: string;
}) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={label}
      disabled={disabled}
      className="rp-ai-switch"
      onClick={() => onChange(!checked)}
    />
  );
}

function DictationSettingsPanel() {
  const [mode, setMode] = useSttMode();
  const [ccEnabled, setCcEnabled] = useCrossCheckEnabled();
  const [ccUbag, setCcUbag] = useUseUbag();

  return (
    <div className="rp-panel rp-anim-fade-in-up">
      <div className="rp-ai-panel-head">
        <div className="rp-ai-panel-title-row">
          <Settings2 size={15} strokeWidth={1.8} aria-hidden />
          <div className="rp-panel-title">Dictation &amp; cross-check preferences</div>
        </div>
      </div>

      <SettingRow
        label="Speech recognition mode"
        description="Auto picks the best on-device setup for your machine."
        control={
          <div className="rp-ai-segmented" role="group" aria-label="Speech recognition mode">
            {STT_MODES.map((m) => (
              <button
                key={m}
                type="button"
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
        description="Cross-check each dictation with a second on-device engine."
        control={
          <Switch
            checked={mode === 'ensemble'}
            onChange={(next) => setMode(next ? 'ensemble' : 'single')}
            label="Dual-engine cross-check"
          />
        }
      />

      <SettingRow
        label="Manual cross check"
        description="Re-run a dictation through extra engines and highlight suggestions."
        control={
          <Switch checked={ccEnabled} onChange={setCcEnabled} label="Manual cross check" />
        }
      />

      <SettingRow
        label="Cross Check via UBAG (no PHI)"
        description="Route the medical-accuracy review through the UBAG cloud AI."
        control={
          <Switch
            checked={ccUbag}
            disabled={!ccEnabled}
            onChange={setCcUbag}
            label="Cross check via UBAG"
          />
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
      <div className="rp-ai-links">
        {QUICK_LINKS.map(({ href, title, description, icon: Icon }) => (
          <Link key={href} href={href} className="rp-ai-link-card">
            <div className="rp-ai-link-icon" aria-hidden>
              <Icon size={20} strokeWidth={1.7} />
            </div>
            <div className="rp-ai-link-body">
              <p className="rp-ai-link-title">{title}</p>
              <p className="rp-ai-link-desc">{description}</p>
            </div>
            <ArrowRight size={16} strokeWidth={1.8} aria-hidden className="rp-ai-link-arrow" />
          </Link>
        ))}
      </div>
    </div>
  );
}
