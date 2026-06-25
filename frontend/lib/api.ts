/**
 * Tiny typed API client used by every Next.js page in the RadioPad
 * reporting workspace. The base URL is read from `NEXT_PUBLIC_API_BASE`
 * (set by Tauri / Capacitor builds) and falls back to the dev proxy
 * configured in `next.config.ts`.
 */

type PublicProcess = { env?: Record<string, string | undefined> };

const RUNTIME_PUBLIC_ENV =
  (globalThis as typeof globalThis & { process?: PublicProcess }).process?.env ?? {};
const BUILD_PUBLIC_ENV: Record<string, string | undefined> = {
  NODE_ENV: process.env.NODE_ENV,
  NEXT_PUBLIC_API_BASE: process.env.NEXT_PUBLIC_API_BASE,
  NEXT_PUBLIC_ALLOW_DEV_LOGIN: process.env.NEXT_PUBLIC_ALLOW_DEV_LOGIN,
  NEXT_PUBLIC_ENABLE_SSO: process.env.NEXT_PUBLIC_ENABLE_SSO,
  NEXT_PUBLIC_STRIPE_PRICE_TEAM: process.env.NEXT_PUBLIC_STRIPE_PRICE_TEAM,
  NEXT_PUBLIC_STRIPE_PRICE_ENTERPRISE: process.env.NEXT_PUBLIC_STRIPE_PRICE_ENTERPRISE,
};

export function publicEnv(name: string): string | undefined {
  return (BUILD_PUBLIC_ENV[name] ?? RUNTIME_PUBLIC_ENV[name]);
}

const CONFIGURED_API_BASE = (publicEnv('NEXT_PUBLIC_API_BASE') || '').replace(/\/+$/, '');
let resolvedApiBase: string | null = null;

async function apiBase(): Promise<string> {
  if (resolvedApiBase !== null) return resolvedApiBase;
  if (CONFIGURED_API_BASE) {
    resolvedApiBase = CONFIGURED_API_BASE;
    return resolvedApiBase;
  }
  if (typeof window !== 'undefined') {
    const tauri = (window as typeof window & {
      __TAURI__?: {
        core?: { invoke?: (cmd: string) => Promise<unknown> };
        invoke?: (cmd: string) => Promise<unknown>;
      };
    }).__TAURI__;
    const invoke = tauri?.core?.invoke ?? tauri?.invoke;
    try {
      const backendUrl = await invoke?.('get_backend_url');
      if (typeof backendUrl === 'string' && backendUrl.length > 0) {
        resolvedApiBase = backendUrl.replace(/\/$/, '');
        return resolvedApiBase;
      }
    } catch {
      /* regular browser/dev proxy path */
    }
  }
  resolvedApiBase = '';
  return resolvedApiBase;
}

export async function apiUrl(path: string): Promise<string> {
  const base = await apiBase();
  return `${base}${path}`;
}

const TENANT_HEADER = 'X-RadioPad-Tenant';
const USER_HEADER = 'X-RadioPad-User';

function activeTenant(): string | null {
  if (typeof window === 'undefined') return null;
  return localStorage.getItem('radiopad.tenant');
}
function activeUser(): string | null {
  if (typeof window === 'undefined') return null;
  return localStorage.getItem('radiopad.user');
}

/**
 * In-memory cache of the bearer token from `secureAuth`. We refuse to await
 * a dynamic import on every request — `setActiveAuthToken` keeps this in
 * sync with the OS-level secure store at app startup and after sign-in.
 */
let cachedAuthToken: string | null = null;
export function setActiveAuthToken(token: string | null): void {
  cachedAuthToken = token;
}
export function getActiveAuthToken(): string | null {
  return cachedAuthToken;
}

function applyAuthHeader(headers: Headers): void {
  if (cachedAuthToken) {
    headers.set('Authorization', `Bearer ${cachedAuthToken}`);
  }
}

function applyTenantHeaders(headers: Headers): void {
  const tenant = activeTenant();
  const user = activeUser();
  if (tenant && user) {
    headers.set(TENANT_HEADER, tenant);
    headers.set(USER_HEADER, user);
  }
}

function requestCredentials(base: string, explicit?: RequestCredentials): RequestCredentials {
  if (explicit) return explicit;
  return base ? 'include' : 'same-origin';
}

function normalizeRequestPath(path: string, init?: RequestInit): string {
  const method = (init?.method ?? 'GET').toUpperCase();
  if (method === 'GET' || method === 'HEAD' || !path.startsWith('/api/auth/')) return path;
  const queryIndex = path.indexOf('?');
  const route = queryIndex >= 0 ? path.slice(0, queryIndex) : path;
  const query = queryIndex >= 0 ? path.slice(queryIndex) : '';
  return route.endsWith('/') ? path : `${route}/${query}`;
}

async function hydrateAuthTokenFromSecureStore(): Promise<boolean> {
  if (cachedAuthToken || typeof window === 'undefined') return false;
  try {
    const { getAuthToken } = await import('./secureAuth');
    const token = await getAuthToken();
    if (!token) return false;
    setActiveAuthToken(token);
    return true;
  } catch {
    return false;
  }
}

/**
 * fetch() but tolerant of a backend that is briefly unreachable. On the bundled
 * desktop the Next.js webview boots a few seconds before the .NET sidecar has
 * finished binding 127.0.0.1:7457 (cold start runs EF migrations + dev seed), so
 * the very first me()/reports requests would otherwise throw "Failed to fetch"
 * and strand the app on a "Backend not reachable / Signed out" screen until a
 * manual reload. A thrown error means no response was received, so for
 * idempotent reads (GET/HEAD) we retry with a short backoff to bridge that boot
 * gap. Mutating verbs are never retried here (no risk of a double-submit).
 */
async function fetchOnce(url: string, init: RequestInit, headers: Headers): Promise<Response> {
  // Only the bundled desktop has the webview-vs-sidecar boot race; the hosted web
  // API is always up. Scoping the retry to the desktop keeps web/test behaviour
  // (a single fetch that fails fast) unchanged.
  const isDesktop = typeof window !== 'undefined' && '__TAURI__' in window;
  const method = (init.method || 'GET').toUpperCase();
  const retriable = isDesktop && (method === 'GET' || method === 'HEAD');
  // ~18s budget (30 x 600ms). A FRESH sidecar process (relaunch / first run) does
  // migrate + schema-bridge + dev-seed + hosted-service startup before it binds the
  // port; that cold boot was observed to exceed the old 6s budget, stranding the
  // webview at "Signed out / backend not ready" and causing transient 500s on the
  // first page loads. 18s comfortably bridges it; web/test stay single-shot.
  const maxAttempts = retriable ? 30 : 1;
  let lastErr: unknown;
  for (let attempt = 0; attempt < maxAttempts; attempt++) {
    try {
      return await fetch(url, { ...init, headers });
    } catch (e) {
      // A network/connection failure surfaces as TypeError ("Failed to fetch").
      lastErr = e;
      if (attempt === maxAttempts - 1 || !(e instanceof TypeError)) throw e;
      await new Promise((r) => setTimeout(r, 600));
    }
  }
  throw lastErr;
}

async function fetchWithAuthRetry(
  url: string,
  init: RequestInit,
  headers: Headers,
): Promise<Response> {
  let res = await fetchOnce(url, init, headers);
  if ((res.status === 401 || res.status === 403) && !cachedAuthToken && await hydrateAuthTokenFromSecureStore()) {
    applyAuthHeader(headers);
    res = await fetchOnce(url, init, headers);
  }
  return res;
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const headers = new Headers(init?.headers || {});
  if (!headers.has('Content-Type')) headers.set('Content-Type', 'application/json');
  applyTenantHeaders(headers);
  applyAuthHeader(headers);
  const base = await apiBase();
  const normalizedPath = normalizeRequestPath(path, init);
  const res = await fetchWithAuthRetry(`${base}${normalizedPath}`, {
    ...init,
    credentials: requestCredentials(base, init?.credentials),
  }, headers);
  if (!res.ok) {
    let body: unknown = null;
    try {
      body = await res.json();
    } catch {
      body = await res.text();
    }
    throw Object.assign(new Error(`API ${res.status} ${res.statusText}`), { status: res.status, body });
  }
  if (res.status === 204) return undefined as unknown as T;
  const ct = res.headers.get('content-type') || '';
  if (ct.includes('application/json') || ct.includes('application/fhir+json')) {
    return (await res.json()) as T;
  }
  return (await res.text()) as unknown as T;
}

async function requestPaged<T>(path: string): Promise<{ items: T[]; total: number }> {
  const headers = new Headers();
  headers.set('Content-Type', 'application/json');
  applyTenantHeaders(headers);
  applyAuthHeader(headers);
  const base = await apiBase();
  const res = await fetchWithAuthRetry(`${base}${path}`, {
    credentials: requestCredentials(base),
  }, headers);
  if (!res.ok) throw Object.assign(new Error(`API ${res.status} ${res.statusText}`), { status: res.status });
  const items = (await res.json()) as T[];
  const total = Number(res.headers.get('X-Total-Count') ?? items.length);
  return { items, total };
}

async function requestBlob(path: string): Promise<Blob> {
  const headers = new Headers();
  applyTenantHeaders(headers);
  applyAuthHeader(headers);
  const base = await apiBase();
  const res = await fetchWithAuthRetry(`${base}${path}`, {
    credentials: requestCredentials(base),
  }, headers);
  if (!res.ok) {
    let body: unknown = null;
    try { body = await res.json(); } catch { body = await res.text(); }
    throw Object.assign(new Error(`API ${res.status} ${res.statusText}`), { status: res.status, body });
  }
  return await res.blob();
}

async function requestForm<T>(path: string, form: FormData): Promise<T> {
  // Multipart upload: deliberately do NOT set Content-Type — the browser adds
  // the multipart boundary. Tenant + auth headers still apply.
  const headers = new Headers();
  applyTenantHeaders(headers);
  applyAuthHeader(headers);
  const base = await apiBase();
  const res = await fetchWithAuthRetry(`${base}${path}`, {
    method: 'POST',
    body: form,
    credentials: requestCredentials(base),
  }, headers);
  if (!res.ok) {
    let body: unknown = null;
    try { body = await res.json(); } catch { body = await res.text(); }
    throw Object.assign(new Error(`API ${res.status} ${res.statusText}`), { status: res.status, body });
  }
  const ct = res.headers.get('content-type') || '';
  if (ct.includes('application/json')) return (await res.json()) as T;
  return (await res.text()) as unknown as T;
}

export type Report = {
  id: string;
  tenantId: string;
  status: 'Draft' | 'Validated' | 'Acknowledged' | 'Exported' | number;
  rulebookId: string | null;
  templateId: string | null;
  study: {
    accessionNumber: string;
    modality: string;
    bodyPart: string;
    indication: string;
    comparison: string;
  };
  indication: string;
  technique: string;
  comparison: string;
  findings: string;
  impression: string;
  recommendations: string;
  aiHighlightsJson: string;
  updatedAt: string;
};

export type Rulebook = {
  id: string;
  rulebookId: string;
  name: string;
  version: string;
  owner: string;
  status: number | string;
  appliesToModalities: string;
  appliesToBodyParts: string;
  /** Iter-34 GOV-001 — last edit timestamp. The rulebooks list endpoint emits this; absent on legacy stores. */
  updatedAt?: string;
};

export type ReportTemplate = {
  id: string;
  templateId: string;
  name: string;
  modality: string;
  bodyPart: string;
  subspecialty: string;
  sectionsJson: string;
  updatedAt: string;
  /** Iter-34 GOV-001 — `TemplateStatus` enum: 0 Draft / 1 Approved / 2 Deprecated / 3 Review. */
  status?: number;
  /** Iter-34 GOV-001 — set when an admin approves the template. */
  approvedAt?: string | null;
};

export type Provider = {
  id: string;
  name: string;
  adapter: string;
  model: string;
  endpointUrl: string;
  compliance: number;
  enabled: boolean;
  priority: number;
  apiKeyConfigured: boolean;
  /** Iter-32 AI-010 — operator-supplied quality score in [0,1] used by the composite cost router. */
  quality?: number;
  /** Iter-34 PROV-009 — operator-supplied free-text data-retention label (e.g. `no-egress`, `30d-soft-delete`, `baa-30d`). Informational; never weakens the PHI policy. */
  retentionLabel?: string;
};

export type UbagHealth = {
  ok: boolean;
  status: string;
  version?: string | null;
  error?: string | null;
};

export type UbagBrowserSummary = {
  instances: number;
  contexts: number;
  tabs: number;
  status?: string | null;
  rawStatus?: string | null;
};

export type UbagTarget = {
  id: string;
  name: string;
  status: string;
  ready: boolean;
  url?: string | null;
};

export type UbagJob = {
  id: string;
  target: string;
  status: string;
  terminal: boolean;
  output?: string | null;
  error?: string | null;
  manualAction?: string | null;
  latencyMs?: number | null;
  rawJson: string;
};

export type UbagWorkflow = {
  id: string;
  status: string;
  rawJson: string;
};

export type UbagWorkflowRun = {
  id: string;
  workflowId: string;
  status: string;
  terminal: boolean;
  output?: string | null;
  error?: string | null;
  manualAction?: string | null;
  rawJson: string;
};

export type UbagStatus = {
  health: UbagHealth;
  browser: UbagBrowserSummary;
  targets: UbagTarget[];
  allowedTargets: string[];
  orderedTargets: string[];
};

export type CopilotSettings = {
  enabled: boolean;
  emergencyDisabled: boolean;
  defaultMode: string;
  allowedModes: string[];
  gitHubEnterpriseSlug: string;
  gitHubOrganization: string;
  gitHubHost: string;
  sdkRuntimeEnabled: boolean;
  cliRuntimeEnabled: boolean;
  allowByoAccounts: boolean;
  allowEnvironmentTokenAuth: boolean;
  requireOsKeychainForCli: boolean;
  promptLoggingEnabled: boolean;
  contextLoggingEnabled: boolean;
  retentionPolicy: string;
  policyJson: string;
  gitHubAppId: string;
  gitHubAppInstallationId: string;
  oAuthClientId: string;
  gitHubAppPrivateKeyConfigured: boolean;
  oAuthClientSecretConfigured: boolean;
  gitHubAppPrivateKeySecretRef?: string | null;
  oAuthClientSecretRef?: string | null;
};

export type CopilotStatus = {
  enabled: boolean;
  emergencyDisabled: boolean;
  defaultMode: string;
  runtimeStatus: string;
  kind: string;
  message: string;
  allowedModes: string[];
  phiBlocked: boolean;
  promptLoggingEnabled: boolean;
  contextLoggingEnabled: boolean;
  gitHubHost: string;
  gitHubOrganization: string;
  unsupportedFeatures: string[];
};

export type CopilotAccount = {
  mode: string;
  gitHubLogin: string;
  tokenStatus: string;
  ssoStatus: string;
  seatStatus: string;
  denialReason: string;
  lastAuthenticatedAt: string | null;
  revokedAt: string | null;
  entitlementAllowed: boolean;
  entitlementSource: string;
};

export type CopilotChatError = {
  kind: string;
  message: string;
  runtimeStatus: string;
  requestId: string;
};

export type CopilotEntitlement = {
  allowed: boolean;
  mode: string;
  source: string;
  gitHubLogin: string;
  ssoStatus: string;
  seatStatus: string;
  denialReason: string;
  checkedAt: string;
  expiresAt: string | null;
};

export type CopilotAuthStart = {
  mode: string;
  kind: string;
  message: string;
  authorizationUrl: string | null;
  desktopCommand: string | null;
  state: string;
};

export type CopilotContextItem = {
  kind: string;
  label: string;
  text: string;
};

export type CopilotContextPreview = {
  messageHash: string;
  containsPhi: boolean;
  included: CopilotContextItem[];
  removed: { label: string; reason: string }[];
  contextHash: string;
};

export type CopilotSession = {
  sessionId: string;
  status: string;
  mode: string;
  runtime: string;
  message: string;
  output: string | null;
  errorKind: string | null;
  context: CopilotContextPreview;
  latencyMs: number;
};

export type CopilotQuotaPolicy = {
  id?: string | null;
  scopeType: string;
  scopeKey: string;
  feature: string;
  windowSeconds: number;
  maxRequests: number;
  maxConcurrent: number;
  enabled: boolean;
};

export type CopilotUsageSummary = {
  total: number;
  completed: number;
  blocked: number;
  failed: number;
  cancelled: number;
  running: number;
  byStatus: { status: string; count: number }[];
};

export type ValidationFinding = {
  ruleId: string;
  severity: 'Info' | 'Warning' | 'Blocker';
  message: string;
  section?: string | null;
  snippet?: string | null;
};

export type ValidationResult = { blockerPresent: boolean; findings: ValidationFinding[]; qualityScore: number };

export type QualityTrendPeriod = {
  period: string;
  avgScore: number;
  reportCount: number;
  blockerCount: number;
};

export type QualityByRadiologist = {
  userId: string;
  email: string;
  avgScore: number;
  reportCount: number;
};

export type QualityByRulebook = {
  rulebookId: string;
  avgScore: number;
  reportCount: number;
};

export type QualityTrendsResponse = {
  trends: QualityTrendPeriod[];
  byRadiologist: QualityByRadiologist[];
  byRulebook: QualityByRulebook[];
};

export type RewriteMode =
  | 'concise'
  | 'formal'
  | 'patient_friendly'
  | 'referring_summary'
  | 'in_my_style';

export type RewriteResult = {
  text: string;
  mode: RewriteMode | string;
  provider?: string;
  model?: string;
  sections?: string[] | null;
  promptVersion?: string;
  latencyMs?: number;
  styleFingerprint?: string;
};

export type ComparePriorSection = {
  section: string;
  current: string;
  prior: string;
  changed: boolean;
};

export type ComparePriorResult = {
  current: { id: string; bodyPart: string };
  prior: { id: string; bodyPart: string; createdAt: string } | null;
  sections: ComparePriorSection[];
};

export type UsageSummary = {
  window: { from: string; to: string };
  totalRequests: number;
  okCount: number;
  blockedCount: number;
  errorCount: number;
  inputTokens: number;
  outputTokens: number;
  avgLatencyMs: number;
  /** Iter-34 BILL-005 — sum of `byProvider[].costTotalUsd`. */
  costTotalUsd: number;
  byProvider: Array<{
    provider: string;
    adapter: string;
    requests: number;
    inputTokens: number;
    outputTokens: number;
    /** Iter-34 BILL-005 — priced via current `ProviderConfig`. */
    costInputUsd: number;
    costOutputUsd: number;
    costTotalUsd: number;
    /** True when no current provider config matches this row's name. */
    unpriced: boolean;
  }>;
};

export type ReportSignature = {
  id: string;
  reportId: string;
  radiologistEmail: string;
  role: 'Primary' | 'CoSigner' | 'Addendum' | string;
  note: string | null;
  signedAt: string;
  body?: string | null;
};

export type RadLexHit = {
  code: string;
  preferredName: string;
  synonyms?: string[] | null;
  definition?: string | null;
};

// Backend wire shape for /api/terminology/radlex/search.
type RadLexHitWire = {
  rid: string;
  preferredLabel: string;
  synonyms?: string[] | null;
  category?: string | null;
};

// Backend wire shape for /api/terminology/rads.
type RadsCategoryWire = {
  code: string;
  shortLabel: string;
  publicGuidanceUrl?: string | null;
};
type RadsSystemWire = {
  system: string;
  description?: string | null;
  publicGuidanceUrl?: string | null;
  categories: RadsCategoryWire[];
};

export type RadsEntry = {
  system: string;
  code: string;
  label: string;
  description?: string | null;
};

export type FhirImportResult = {
  reportId: string;
  status: string;
  warnings?: string[];
};

// added by billing-dashboard agent — Agent 2 wires impl
export type BillingStatus = {
  plan: 'Trial' | 'Team' | 'Enterprise';
  subscriptionStatus: string | null;
  trialEndsAt: string | null;
  gracePeriodUntil: string | null;
  suspendedAt: string | null;
  currentPeriodEnd: string | null;
  customerConfigured: boolean;
};

// added by billing-dashboard agent — Agent 2 wires impl
export type BillingInvoice = {
  id: string;
  number: string | null;
  status: string;
  amountDue: number;
  amountPaid: number;
  currency: string;
  hostedInvoiceUrl: string | null;
  invoicePdf: string | null;
  periodStart: string | null;
  periodEnd: string | null;
};

/** PRD BILL-002 / BILL-007 — month-to-date AI credit balance + trial marker. */
export type BillingCredits = {
  plan: 'Trial' | 'Team' | 'Enterprise';
  periodStart: string;
  periodEnd: string;
  used: { calls: number; inputTokens: number; outputTokens: number };
  limits: { calls: number; inputTokens: number; outputTokens: number };
  remaining: { calls: number; inputTokens: number; outputTokens: number };
  trialEndsAt: string | null;
};

export type WebAuthnCredentialRow = {
  id: string;
  label: string;
  signCount: number;
  createdAt: string;
  lastUsedAt: string | null;
};

/** Iter-32 MCP-001..007 — admin registry row. `status` is `0=Submitted | 1=Approved | 2=Blocked`. */
export type McpToolRow = {
  id: string;
  name: string;
  version: string;
  kind: number;
  isBuiltIn: boolean;
  scope: number;
  scopeString: string;
  status: 0 | 1 | 2;
  approved: boolean;
  approvedBy?: string | null;
  approvedAt?: string | null;
  manifestSha256: string;
  manifestSigned: boolean;
  allowedConnectorPaths: string[];
  createdAt: string;
};

/** PRD Beta #7 — structured measurement extracted from report text. */
export type ExtractedMeasurement = {
  value: number;
  unit: string;
  secondValue: string | null;
  thirdValue: string | null;
  anatomicalLocation: string | null;
  finding: string | null;
  laterality: string | null;
  section: string;
  startIndex: number;
  endIndex: number;
};

/** PRD Enterprise GA #13 — Marketplace submission shape returned by GET /api/marketplace/submissions. */
export type MarketplaceSubmission = {
  id: string;
  name: string;
  description: string;
  kind: string;
  status: string;
  version: string;
  installCount: number;
  submittedAt: string | null;
  reviewedAt: string | null;
  reviewNotes: string | null;
  rejectionReason: string | null;
  publisher: string;
  publisherUser: string;
};

export const api = {
  health: () => request<{ status: string }>('/api/health'),
  me: () =>
    request<{
      tenant: { slug: string; displayName: string };
      user: { email: string; role: number; roleName?: string; permissions: string[] };
    }>('/api/tenant/me'),
  reports: {
    list: () => request<Report[]>('/api/reports'),
    listPaged: (params: { modality?: string; status?: number; q?: string; skip?: number; take?: number } = {}) => {
      const sp = new URLSearchParams();
      if (params.modality) sp.set('modality', params.modality);
      if (params.status !== undefined && params.status !== null) sp.set('status', String(params.status));
      if (params.q) sp.set('q', params.q);
      if (params.skip !== undefined) sp.set('skip', String(params.skip));
      if (params.take !== undefined) sp.set('take', String(params.take));
      const qs = sp.toString();
      return requestPaged<Report>(`/api/reports${qs ? '?' + qs : ''}`);
    },
    get: (id: string) => request<Report>(`/api/reports/${id}`),
    create: (body: Partial<Report['study']> & { rulebookId?: string | null; templateId?: string | null }) =>
      request<Report>('/api/reports', { method: 'POST', body: JSON.stringify(body) }),
    patch: (id: string, body: Partial<Report>) =>
      request<Report>(`/api/reports/${id}`, { method: 'PATCH', body: JSON.stringify(body) }),
    /**
     * Iter-36 MOB — append a transcript to the Findings section. The
     * mobile dictation page calls this so a radiologist's spoken notes
     * land on the report without overwriting prior typed content. We
     * fetch + PATCH (no new backend endpoint) so tenant isolation,
     * audit trail, and `RequireZeroBlockers` gating all flow through
     * the existing PATCH route.
     */
    appendFindings: async (id: string, transcript: string): Promise<Report> => {
      const text = (transcript ?? '').trim();
      if (text.length === 0) return await request<Report>(`/api/reports/${id}`);
      const current = await request<Report>(`/api/reports/${id}`);
      const next = current.findings ? `${current.findings.trimEnd()}\n${text}` : text;
      return await request<Report>(`/api/reports/${id}`, {
        method: 'PATCH',
        body: JSON.stringify({ findings: next }),
      });
    },
    validate: (id: string) => request<ValidationResult>(`/api/reports/${id}/validate`, { method: 'POST' }),
    runAi: (
      id: string,
      body: {
        mode:
          | 'impression'
          | 'cleanup'
          | 'draft'
          | 'concise'
          | 'formal'
          | 'patient_friendly'
          | 'referring_summary';
        providerId: string;
      },
    ) =>
      request<{ text: string; provider: string; model: string; latencyMs: number; promptVersion: string; mode: string }>(
        `/api/reports/${id}/ai`,
        { method: 'POST', body: JSON.stringify(body) },
      ),
    prior: (id: string) =>
      request<{ current: { id: string; bodyPart: string }; prior: Report | null }>(
        `/api/reports/${id}/prior`,
      ),
    acknowledge: (id: string) => request<Report>(`/api/reports/${id}/acknowledge`, { method: 'POST' }),
    exportFhir: (id: string) => request<unknown>(`/api/reports/${id}/export/fhir`),
    exportJson: (id: string) => request<unknown>(`/api/reports/${id}/export/json`),
    exportText: (id: string, opts?: { preview?: boolean }) =>
      request<string>(
        `/api/reports/${id}/export/text${opts?.preview ? '?preview=true' : ''}`,
      ),
    exportPdf: (id: string) => requestBlob(`/api/reports/${id}/export/pdf`),
    exportDocx: (id: string) => requestBlob(`/api/reports/${id}/export/docx`),
    exportHl7: (id: string) => requestBlob(`/api/reports/${id}/export/hl7`),
    rewrite: (
      id: string,
      body: { mode: RewriteMode; sections?: string[] | null; providerId?: string; samples?: string[] },
    ) =>
      request<RewriteResult>(`/api/reports/${id}/rewrite`, {
        method: 'POST',
        body: JSON.stringify(body),
      }),
    rewriteInMyStyle: (
      id: string,
      body: { samples: string[]; sections?: string[] | null; providerId?: string },
    ) =>
      request<RewriteResult>(`/api/reports/${id}/rewrite?mode=in_my_style`, {
        method: 'POST',
        body: JSON.stringify({ ...body, mode: 'in_my_style' }),
      }),
    comparePrior: (id: string) =>
      request<ComparePriorResult>(`/api/reports/${id}/compare-prior`),
    cleanupDictation: (id: string, rawDictation: string) =>
      request<{
        provider: string;
        model: string;
        latencyMs: number;
        promptVersion: string;
        cleanedSections: {
          indication: string;
          technique: string;
          findings: string;
          impression: string;
          recommendations: string;
        };
      }>(`/api/reports/${id}/dictation/cleanup`, {
        method: 'POST',
        body: JSON.stringify({ rawDictation }),
      }),
    // High-accuracy transcription: upload the recorded dictation audio; the
    // backend routes it through UBAG (audio attached into a chat model) and
    // returns the transcript. PHI routing is handled by the provider router,
    // exactly like the text dictation/cleanup path — no separate gate.
    transcribe: (id: string, audio: Blob) => {
      const form = new FormData();
      // Desktop converts to 16 kHz mono WAV for the on-device engine; web sends
      // the original webm. Name the part by type so the backend content-type
      // check sees the right format (it keys off the blob's MIME type).
      const name = audio.type.includes('wav') ? 'dictation.wav' : 'dictation.webm';
      form.append('audio', audio, name);
      return requestForm<{ transcript: string; provider: string; model: string; latencyMs: number }>(
        `/api/reports/${id}/dictation/transcribe`,
        form,
      );
    },
    signatures: (id: string) =>
      request<ReportSignature[]>(`/api/reports/${id}/signatures`),
    sign: (id: string, body: { role: 'Primary' | 'CoSigner'; note?: string }) =>
      request<ReportSignature>(`/api/reports/${id}/sign`, {
        method: 'POST',
        body: JSON.stringify(body),
      }),
    addAddendum: (id: string, body: string, note?: string) =>
      request<ReportSignature>(`/api/reports/${id}/addendum`, {
        method: 'POST',
        body: JSON.stringify({ body, note }),
      }),
    measurements: (id: string) => request<ExtractedMeasurement[]>(`/api/reports/${id}/measurements`),
  },
  rulebooks: {
    list: () => request<Rulebook[]>('/api/rulebooks'),
    get: (id: string) => request<Rulebook & { sourceYaml: string }>(`/api/rulebooks/${id}`),
    save: (yaml: string) => request<Rulebook>('/api/rulebooks', { method: 'POST', body: JSON.stringify({ yaml }) }),
    validateYaml: (yaml: string) =>
      request<{ ok: boolean; problems: string[] }>('/api/rulebooks/validate', {
        method: 'POST',
        body: JSON.stringify({ yaml }),
      }),
    approve: (id: string) => request<Rulebook>(`/api/rulebooks/${id}/approve`, { method: 'POST' }),
    deprecate: (id: string) => request<Rulebook>(`/api/rulebooks/${id}/deprecate`, { method: 'POST' }),
    rollback: (id: string, version: string) =>
      request<Rulebook>(`/api/rulebooks/${id}/rollback`, {
        method: 'POST',
        body: JSON.stringify({ version }),
      }),
  },
  providers: {
    list: () => request<Provider[]>('/api/providers'),
    save: (body: Partial<Provider> & { id?: string | null; apiKeySecretRef?: string }) =>
      request<{ id: string }>('/api/providers', { method: 'POST', body: JSON.stringify(body) }),
    health: (id: string) =>
      request<{ ok: boolean; error?: string | null; note?: string | null }>(
        `/api/providers/${id}/health`,
        { method: 'POST' },
      ),
    /**
     * Iter-35 PROV-007 — encrypted refresh-token vault. The status surface
     * never echoes ciphertext; only a `hasToken` boolean and timestamps.
     */
    oauth: {
      status: (id: string) =>
        request<{
          hasToken: boolean;
          updatedAt: string | null;
          expiresAt: string | null;
          rotationPolicy: 'never' | 'before_expiry' | 'every_24h';
        }>(`/api/providers/${id}/oauth/refresh-token/status`),
      save: (
        id: string,
        body: { refreshToken: string; expiresAt?: string | null; rotationPolicy?: string | null },
      ) =>
        request<void>(`/api/providers/${id}/oauth/refresh-token`, {
          method: 'POST',
          body: JSON.stringify(body),
        }),
      delete: (id: string) =>
        request<void>(`/api/providers/${id}/oauth/refresh-token`, { method: 'DELETE' }),
    },
  },
  copilot: {
    status: () => request<CopilotStatus>('/api/copilot/status'),
    account: () => request<CopilotAccount>('/api/copilot/account'),
    entitlement: () => request<CopilotEntitlement>('/api/copilot/entitlement'),
    beginAuth: (body: { mode?: string | null; redirectUri?: string | null }) =>
      request<CopilotAuthStart>('/api/copilot/account/auth/start', {
        method: 'POST',
        body: JSON.stringify(body),
      }),
    linkLocalCli: (body: { gitHubLogin?: string | null; gitHubUserId?: number | null; host?: string | null; ssoStatus?: string | null; seatStatus?: string | null }) =>
      request<CopilotAccount>('/api/copilot/account/local-cli', {
        method: 'POST',
        body: JSON.stringify(body),
      }),
    revokeAccount: () => request<void>('/api/copilot/account', { method: 'DELETE' }),
    previewContext: (body: { message?: string | null; contextKind?: string | null; items?: CopilotContextItem[] | null }) =>
      request<CopilotContextPreview>('/api/copilot/context/preview', {
        method: 'POST',
        body: JSON.stringify(body),
      }),
    startSession: (body: { message?: string | null; mode?: string | null; contextKind?: string | null; context?: CopilotContextItem[] | null }) =>
      request<CopilotSession>('/api/copilot/sessions', {
        method: 'POST',
        body: JSON.stringify(body),
      }),
    cancelSession: (sessionId: string) =>
      request<CopilotSession>(`/api/copilot/sessions/${encodeURIComponent(sessionId)}/cancel`, { method: 'POST' }),
    chat: (body: { message: string; sessionId?: string | null; mode?: string | null; contextKind?: string | null }) =>
      request<CopilotSession | CopilotChatError>('/api/copilot/chat', {
        method: 'POST',
        body: JSON.stringify(body),
      }),
    admin: {
      settings: () => request<CopilotSettings>('/api/copilot/admin/settings'),
      saveSettings: (body: CopilotSettings) =>
        request<CopilotSettings>('/api/copilot/admin/settings', {
          method: 'POST',
          body: JSON.stringify(body),
        }),
      status: () => request<CopilotStatus>('/api/copilot/admin/status'),
      diagnostics: () =>
        request<{ runId: string; status: CopilotStatus; results: unknown }>(
          '/api/copilot/admin/diagnostics',
          { method: 'POST' },
        ),
      toggleFeature: (featureKey: string, body: { featureKey: string; enabled: boolean; requiredRole?: string; policyJson?: string }) =>
        request<{ featureKey: string; enabled: boolean; requiredRole: string; policyJson: string }>(
          `/api/copilot/admin/features/${encodeURIComponent(featureKey)}`,
          { method: 'POST', body: JSON.stringify(body) },
        ),
      quotas: () => request<CopilotQuotaPolicy[]>('/api/copilot/admin/quotas'),
      saveQuotas: (body: CopilotQuotaPolicy[]) =>
        request<CopilotQuotaPolicy[]>('/api/copilot/admin/quotas', {
          method: 'POST',
          body: JSON.stringify(body),
        }),
      usage: () => request<CopilotUsageSummary>('/api/copilot/admin/usage'),
    },
  },
  ai: {
    routingPreview: (params: {
      phi?: boolean;
      modality?: string;
      input?: number;
      output?: number;
    }) => {
      const q = new URLSearchParams();
      if (params.phi) q.set('phi', 'true');
      if (params.modality) q.set('modality', params.modality);
      if (params.input != null) q.set('input', String(params.input));
      if (params.output != null) q.set('output', String(params.output));
      return request<{
        selectedProviderId: string | null;
        selectedProviderName: string | null;
        reason: string | null;
        weights: { cost: number; quality: number; latency: number };
        candidates: Array<{
          providerId: string;
          name: string;
          adapter: string;
          compliance: string;
          eligible: boolean;
          ineligibleReason: string | null;
          costUsdEstimate: number;
          costScore: number;
          qualityScore: number;
          latencyScore: number;
          p95LatencyMs24h: number | null;
          compositeScore: number;
        }>;
      }>(`/api/ai/routing/preview?${q.toString()}`);
    },
    /**
     * PRD PROV-005 (iter-34) — runs the same prompt across up to four
     * sandbox-class providers and returns each output side-by-side. The
     * backend gates on `Tenant.AllowSandboxRulebooks` and refuses any
     * provider whose compliance class is not `Sandbox`.
     */
    sandboxCompare: (body: { reportId: string; mode: string; providerIds: string[] }) =>
      request<{
        runs: Array<{
          providerId: string;
          provider: string;
          model: string;
          output: string | null;
          latencyMs: number;
          inputTokens: number;
          outputTokens: number;
          error: string | null;
        }>;
      }>('/api/ai/sandbox/compare', { method: 'POST', body: JSON.stringify(body) }),
  },
  ubag: {
    status: () => request<UbagStatus>('/api/ubag/status'),
    submitJob: (body: { target: string; prompt: string }) =>
      request<UbagJob>('/api/ubag/jobs', { method: 'POST', body: JSON.stringify(body) }),
    getJob: (id: string) => request<UbagJob>(`/api/ubag/jobs/${encodeURIComponent(id)}`),
    runOrderedWorkflow: (body: { prompt: string; name?: string | null }) =>
      request<{ workflow: UbagWorkflow; run: UbagWorkflowRun; orderedTargets: string[] }>(
        '/api/ubag/workflows/ordered-web-chain',
        { method: 'POST', body: JSON.stringify(body) },
      ),
    getWorkflowRun: (id: string) =>
      request<UbagWorkflowRun>(`/api/ubag/workflows/runs/${encodeURIComponent(id)}`),
  },
  promptOverrides: {
    list: () =>
      request<
        Array<{
          id: string;
          rulebookId: string;
          blockKey: string;
          body: string;
          status: 'Draft' | 'Approved';
          approvedByUserId: string | null;
          approvedAt: string | null;
          updatedAt: string;
        }>
      >('/api/prompts/overrides'),
    save: (body: { id?: string | null; rulebookId: string; blockKey: string; body: string }) =>
      request<{ id: string; status: 'Draft' }>('/api/prompts/overrides', {
        method: 'POST',
        body: JSON.stringify(body),
      }),
    approve: (id: string) =>
      request<{ id: string; status: 'Approved'; approvedAt: string }>(
        `/api/prompts/overrides/${id}/approve`,
        { method: 'POST' },
      ),
    delete: (id: string) =>
      request<void>(`/api/prompts/overrides/${id}`, { method: 'DELETE' }),
    /** PRD §16.4 — list all versions of a prompt override. */
    listVersions: (id: string) =>
      request<PromptOverrideVersion[]>(`/api/prompts/overrides/${id}/versions`),
    /** PRD §16.4 — text diff between two versions of a prompt override. */
    diffVersions: (id: string, v1: number, v2: number) =>
      request<PromptVersionDiff>(
        `/api/prompts/overrides/${id}/diff?v1=${v1}&v2=${v2}`,
      ),
  },
  /** PRD §16.4 — Prompt Studio: run golden cases against a prompt override. */
  promptStudio: {
    testGolden: (body: { rulebookId: string; promptOverrideId?: string | null }) =>
      request<GoldenCaseResult[]>('/api/prompts/test-golden', {
        method: 'POST',
        body: JSON.stringify(body),
      }),
  },
  templates: {
    list: () => request<ReportTemplate[]>('/api/templates'),
    save: (body: {
      templateId: string;
      name: string;
      modality: string;
      bodyPart: string;
      subspecialty?: string;
      sectionsJson: string;
    }) => request<ReportTemplate>('/api/templates', { method: 'POST', body: JSON.stringify(body) }),
    approve: (id: string) =>
      request<ReportTemplate>(`/api/templates/${id}/approve`, { method: 'POST' }),
    submitForReview: (id: string) =>
      request<ReportTemplate>(`/api/templates/${id}/submit-review`, { method: 'POST' }),
    deprecate: (id: string) =>
      request<ReportTemplate>(`/api/templates/${id}/deprecate`, { method: 'POST' }),
    preview: (id: string, reportId?: string) =>
      request<{
        id: string;
        templateId: string;
        name: string;
        modality: string;
        bodyPart: string;
        variant: string;
        status: string;
        sections: Array<{ key: string; label: string; body: string }>;
      }>(`/api/templates/${id}/preview${reportId ? `?reportId=${encodeURIComponent(reportId)}` : ''}`),
    usage: (id: string) =>
      request<{
        templateId: string;
        rowId: string;
        window: { from: string; to: string };
        counts: { last7d: number; last30d: number; last90d: number };
        byUser: Array<{ userId: string; count: number }>;
        byModality: Array<{ modality: string; count: number }>;
      }>(`/api/templates/${id}/usage`),
  },
  /**
   * Iter-35 — versioned clinical validation packs (rulebook golden suites).
   * A pack bundles `{report, expectFlagged}` cases that a rulebook must
   * pass before promotion. Lifecycle: Draft → Approved → Deprecated.
   */
  validationPacks: {
    list: (rulebookId?: string) => {
      const q = rulebookId ? `?rulebookId=${encodeURIComponent(rulebookId)}` : '';
      return request<
        Array<{
          id: string;
          rulebookId: string;
          version: string;
          name: string;
          status: 'Draft' | 'Approved' | 'Deprecated';
          approvedAt: string | null;
          approvedBy: string | null;
          createdAt: string;
          createdBy: string;
          caseCount: number;
        }>
      >(`/api/validation-packs${q}`);
    },
    create: (body: {
      rulebookId: string;
      version: string;
      name?: string;
      goldenCases: Array<{
        name?: string;
        report: unknown;
        expectFlagged?: string[];
      }>;
    }) =>
      request<{ id: string; rulebookId: string; version: string; name: string; status: string }>(
        '/api/validation-packs',
        { method: 'POST', body: JSON.stringify(body) },
      ),
    approve: (id: string) =>
      request<{ id: string; status: 'Approved'; approvedAt: string }>(
        `/api/validation-packs/${id}/approve`,
        { method: 'POST' },
      ),
    deprecate: (id: string) =>
      request<{ id: string; status: 'Deprecated' }>(
        `/api/validation-packs/${id}/deprecate`,
        { method: 'POST' },
      ),
    run: (id: string) =>
      request<{
        passed: number;
        failed: number;
        totalCases: number;
        failures: Array<{ caseId: string; missing: string[]; unexpected: string[] }>;
      }>(`/api/validation-packs/${id}/run`, { method: 'POST' }),
    export: (id: string) =>
      request<{
        id: string;
        rulebookId: string;
        version: string;
        name: string;
        status: string;
        createdAt: string;
        approvedAt: string | null;
        cases: unknown;
      }>(`/api/validation-packs/${id}/export`),
  },
  audit: {
    query: (params: { from?: string; to?: string; take?: number } = {}) => {
      const q = new URLSearchParams();
      if (params.from) q.set('from', params.from);
      if (params.to) q.set('to', params.to);
      if (params.take) q.set('take', String(params.take));
      return request<unknown[]>(`/api/audit?${q}`);
    },
    /**
     * Iter-34 GOV-001 — recompute the SHA-256 integrity chain on the
     * server. Returns `{ intact:true, … }` on 200, normalises the 422
     * `audit_chain_broken` problem body into `{ intact:false, … }` so
     * the governance dashboard can render a single discriminated union.
     */
    verify: async (): Promise<{
      intact: boolean;
      eventCount: number;
      firstBrokenEventId?: string | null;
      lastVerifiedAt: string | null;
    }> => {
      try {
        const ok = await request<{ intact: boolean; eventCount: number; lastVerifiedAt: string | null }>(
          '/api/audit/verify',
        );
        return { ...ok, firstBrokenEventId: null };
      } catch (e) {
        const err = e as {
          status?: number;
          body?: {
            kind?: string;
            eventCount?: number;
            firstBrokenEventId?: string | null;
            lastVerifiedAt?: string | null;
          };
        };
        if (err.status === 422 && err.body?.kind === 'audit_chain_broken') {
          return {
            intact: false,
            eventCount: err.body.eventCount ?? 0,
            firstBrokenEventId: err.body.firstBrokenEventId ?? null,
            lastVerifiedAt: err.body.lastVerifiedAt ?? null,
          };
        }
        throw e;
      }
    },
  },
  lexicon: {
    list: () =>
      request<
        { id: string; term: string; forbidden: boolean; replacement: string; note: string }[]
      >('/api/lexicon'),
    save: (body: {
      id?: string | null;
      term: string;
      forbidden: boolean;
      replacement?: string;
      note?: string;
    }) =>
      request<{ id: string }>('/api/lexicon', {
        method: 'POST',
        body: JSON.stringify(body),
      }),
    delete: (id: string) =>
      request<void>(`/api/lexicon/${id}`, { method: 'DELETE' }),
    importCsv: async (csv: string, replaceAll = false): Promise<{ upserts: number; removed: number }> => {
      const headers = new Headers();
      headers.set('Content-Type', 'text/csv');
      applyTenantHeaders(headers);
      applyAuthHeader(headers);
      const base = await apiBase();
      const res = await fetch(`${base}/api/lexicon/import-csv?replaceAll=${replaceAll ? 'true' : 'false'}`, {
        method: 'POST',
        headers,
        body: csv,
        credentials: requestCredentials(base),
      });
      if (!res.ok) {
        let body: unknown = null;
        try { body = await res.json(); } catch { body = await res.text(); }
        throw Object.assign(new Error(`API ${res.status} ${res.statusText}`), { status: res.status, body });
      }
      return (await res.json()) as { upserts: number; removed: number };
    },
  },
  tenant: {
    settings: {
      get: () =>
        request<{
          id: string;
          hallucinationDetectionEnabled: boolean;
          hallucinationSeverity: 'Info' | 'Warning' | 'Blocker';
          hallucinationAllowList: string;
          hallucinationMinSupport: number;
          plan: 0 | 1 | 2;
          featureFlagsJson: string;
          ipAllowlistJson: string;
          ingest: { bearerConfigured: boolean };
          dicomWeb: { baseUrl: string; bearerConfigured: boolean };
          pacs: { vendor: 'sectra' | 'visage' | 'carestream' | null };
          stripe: {
            customerId: string | null;
            subscriptionId: string | null;
            status: string | null;
            currentPeriodEnd: string | null;
          };
          retention: {
            days: number;
            hashOnlyAuditMode: boolean;
            legalHold: boolean;
          };
          scim: { bearerConfigured: boolean };
          // Iter-32 SEC-003 — customer-managed key (CMK) status. The
          // `keyRef` is opaque (e.g. `aws:arn:...`); the backend never
          // returns wrapped key material here.
          cmk?: {
            keyRef: string | null;
            lastVerifiedAt: string | null;
            configured: boolean;
          };
          validation: {
            requireZeroBlockers: boolean;
            warnAsBlocker: boolean;
          };
        }>('/api/tenant/settings'),
      save: (body: {
        hallucinationDetectionEnabled?: boolean;
        hallucinationSeverity?: 'Info' | 'Warning' | 'Blocker';
        hallucinationAllowList?: string;
        hallucinationMinSupport?: number;
        plan?: 0 | 1 | 2;
        featureFlagsJson?: string;
        ipAllowlistJson?: string;
        ingestBearerSecret?: string | null;
        dicomWebBaseUrl?: string | null;
        dicomWebBearerSecret?: string | null;
        pacsVendor?: 'sectra' | 'visage' | 'carestream' | '' | null;
        retentionDays?: number | null;
        hashOnlyAuditMode?: boolean | null;
        legalHold?: boolean | null;
        scimBearerSecret?: string | null;
        cmkKeyRef?: string | null;
        cmkVerified?: boolean | null;
        requireZeroBlockers?: boolean | null;
        warnAsBlocker?: boolean | null;
      }) =>
        request<{ id: string }>('/api/tenant/settings', {
          method: 'POST',
          body: JSON.stringify(body),
        }),
      verifyKms: () =>
        request<{
          ok: boolean;
          scheme: string;
          keyRef: string;
          lastVerifiedAt: string | null;
        }>('/api/tenant/settings/kms/verify', { method: 'POST' }),
      /** Iter-35 i18n — read the tenant default UI locale + supported set. */
      getLocale: () =>
        request<{ locale: string; supported: string[] }>('/api/tenant/settings/locale'),
      /** Iter-35 i18n — write the tenant default UI locale (IT-Admin / Medical Director). */
      setLocale: (locale: string) =>
        request<{ locale: string }>('/api/tenant/settings/locale', {
          method: 'PUT',
          body: JSON.stringify({ locale }),
        }),
    },
  },
  users: {
    me: {
      /** Iter-35 i18n — set or clear the per-user locale override (any tenant member). */
      setLocale: (locale: string | null) =>
        request<{ locale: string | null }>('/api/users/me/locale', {
          method: 'PUT',
          body: JSON.stringify({ locale }),
        }),
    },
  },
  billing: {
    checkout: (priceId: string, successUrl: string, cancelUrl: string) =>
      request<{ id: string; url: string }>('/api/billing/checkout', {
        method: 'POST',
        body: JSON.stringify({ priceId, successUrl, cancelUrl }),
      }),
    portal: (returnUrl: string) =>
      request<{ url: string }>('/api/billing/portal', {
        method: 'POST',
        body: JSON.stringify({ returnUrl }),
      }),
    features: () =>
      request<{ plan: string; features: Record<string, boolean> }>('/api/billing/features'),
    // added by billing-dashboard agent — Agent 2 wires impl
    status: () =>
      request<BillingStatus>('/api/billing/status'),
    // added by billing-dashboard agent — Agent 2 wires impl
    invoices: () =>
      request<BillingInvoice[]>('/api/billing/invoices'),
    /** PRD BILL-002 / BILL-007 — month-to-date AI credit balance + trial marker. */
    credits: () =>
      request<BillingCredits>('/api/billing/credits'),
    refund: (b: { paymentIntentId: string; amountCents?: number; reason?: string }) =>
      request<{ id: string; status: string; amount: number }>('/api/billing/refund', {
        method: 'POST',
        body: JSON.stringify(b),
      }),
    bulkExport: (params: { from: string; to: string; format: 'csv' | 'zip' }) => {
      const sp = new URLSearchParams();
      sp.set('from', params.from);
      sp.set('to', params.to);
      sp.set('format', params.format);
      return requestBlob(`/api/billing/invoices/export?${sp.toString()}`);
    },
  },
  auth: {
    oidcAuthorizeUrl: (returnUrl?: string) => {
      const q = returnUrl ? `?returnUrl=${encodeURIComponent(returnUrl)}` : '';
      return request<{ url: string }>(`/api/auth/oidc/authorize-url${q}`).then((r) => r.url);
    },
    logout: () =>
      request<void>('/api/auth/logout/', { method: 'POST' }),
    session: () =>
      request<{ tenant: { slug: string; displayName: string }; user: { email: string; role?: number } }>(
        '/api/auth/session',
      ),
    signIn: (tenant: string, user: string) =>
      request<{ token: string; tenant: string; user: string; expiresAt: string }>(
        '/api/auth/signin',
        { method: 'POST', body: JSON.stringify({ tenant, user }) },
      ),
    mfaEnroll: (tenant: string, email: string) =>
      request<{ secret: string; otpauth: string }>('/api/auth/mfa/enroll', {
        method: 'POST',
        body: JSON.stringify({ tenant, email }),
      }),
    mfaVerify: (tenant: string, email: string, code: string) =>
      request<{ ok: boolean; mfaEnabled: boolean }>('/api/auth/mfa/verify', {
        method: 'POST',
        body: JSON.stringify({ tenant, email, code }),
      }),
    magicLinkRequest: (tenant: string, email: string, callbackUrl?: string) =>
      request<{ ok: boolean; devLink?: string }>('/api/auth/magic-link/request', {
        method: 'POST',
        body: JSON.stringify({ tenant, email, callbackUrl }),
      }),
    magicLinkConsume: (token: string) =>
      request<{ token: string; tenant: string; user: string; expiresAt: string }>(
        '/api/auth/magic-link/consume',
        { method: 'POST', body: JSON.stringify({ token }) },
      ),
    deviceApprove: (tenant: string, email: string, userCode: string) =>
      request<{ ok: boolean }>('/api/auth/device/approve', {
        method: 'POST',
        body: JSON.stringify({ tenant, email, userCode }),
      }),
    deviceDeny: (tenant: string, email: string, userCode: string) =>
      request<{ ok: boolean }>('/api/auth/device/deny', {
        method: 'POST',
        body: JSON.stringify({ tenant, email, userCode }),
      }),
    /**
     * PRD DESK-008 / AUTH-007 — RFC 8628 device authorization grant.
     * The desktop shell calls `deviceAuthorize` to obtain `(deviceCode, userCode)`,
     * displays the `userCode` to the user, and then polls `deviceToken` until
     * the operator approves the pairing through the web app's `/devices` page.
     */
    deviceAuthorize: (clientId: string, deviceFingerprint?: string) =>
      request<{
        deviceCode: string;
        userCode: string;
        verificationUri: string;
        verificationUriComplete: string;
        expiresIn: number;
        interval: number;
      }>('/api/auth/device/authorize', {
        method: 'POST',
        body: JSON.stringify({ clientId, deviceFingerprint }),
      }),
    deviceToken: (deviceCode: string) =>
      request<{
        accessToken: string;
        tokenType: string;
        expiresIn: number;
        tenant: string;
        user: string;
      }>('/api/auth/device/token', {
        method: 'POST',
        body: JSON.stringify({
          deviceCode,
          grantType: 'urn:ietf:params:oauth:grant-type:device_code',
        }),
      }),
    webAuthnCredentials: () =>
      request<WebAuthnCredentialRow[]>('/api/auth/webauthn/credentials'),
    webAuthnSignInOptions: () =>
      request<{
        challenge: string;
        rpId?: string;
        timeout?: number;
        userVerification?: UserVerificationRequirement;
        allowCredentials?: Array<{ type: 'public-key'; id: string }>;
      }>('/api/auth/webauthn/signin-options', { method: 'POST', body: JSON.stringify({}) }),
    webAuthnSignIn: (body: {
      credentialId: string;
      clientDataJson: string;
      authenticatorData: string;
      signature: string;
      signCount: number;
    }) =>
      request<{ token: string; tenant: string; user: string; expiresAt: string }>(
        '/api/auth/webauthn/signin',
        { method: 'POST', body: JSON.stringify(body) },
      ),
  },
  security: {
    testWebhook: () =>
      request<{ sent: boolean; statusCode?: number | null; configured: boolean }>(
        '/api/admin/security/test-webhook',
        { method: 'POST' },
      ),
  },
  marketplace: {
    list: (kind?: string) => {
      const q = kind ? `?kind=${encodeURIComponent(kind)}` : '';
      return request<Array<{ id: string; name: string; description: string; kind: string; priceCents: number; reviewedAt: string }>>(`/api/marketplace/listings${q}`);
    },
    get: (id: string) => request<unknown>(`/api/marketplace/listings/${id}`),
    create: (body: { name: string; description: string; kind: string; artifactBody: string; priceCents: number }) =>
      request<{ id: string; status: string }>('/api/marketplace/listings', {
        method: 'POST', body: JSON.stringify(body),
      }),
    submit: (id: string) => request<{ status: string }>(`/api/marketplace/listings/${id}/submit`, { method: 'POST' }),
    approve: (id: string) => request<{ status: string; stripePriceId?: string }>(`/api/marketplace/listings/${id}/approve`, { method: 'POST' }),
    reject: (id: string, reason: string) =>
      request<{ status: string }>(`/api/marketplace/listings/${id}/reject`, {
        method: 'POST', body: JSON.stringify({ reason }),
      }),
    checkout: (id: string, returnUrl: string) =>
      request<{ url?: string; granted?: boolean; purchaseId: string }>(
        `/api/marketplace/listings/${id}/checkout?returnUrl=${encodeURIComponent(returnUrl)}`,
        { method: 'POST' },
      ),
    connectOnboarding: (returnUrl: string) =>
      request<{ url: string }>(
        `/api/marketplace/connect/onboarding?returnUrl=${encodeURIComponent(returnUrl)}`,
        { method: 'POST' },
      ),
    connectStatus: () =>
      request<{ onboarded: boolean; chargesEnabled: boolean; payoutsEnabled: boolean; requirements?: string[] }>(
        '/api/marketplace/connect/status',
      ),
    refund: (id: string, body: { reason?: string }) =>
      request<{ id: string; status: string; amount: number }>(
        `/api/marketplace/purchases/${id}/refund`,
        { method: 'POST', body: JSON.stringify(body) },
      ),
    // ─── PRD Enterprise GA #13: Submission & Approval Workflow ───
    submitForReview: (body: { category: string; sourceId: string; version: string; description?: string }) =>
      request<{ id: string; status: string }>('/api/marketplace/submissions', {
        method: 'POST', body: JSON.stringify(body),
      }),
    listSubmissions: () =>
      request<Array<MarketplaceSubmission>>('/api/marketplace/submissions'),
    approveSubmission: (id: string) =>
      request<{ status: string }>(`/api/marketplace/submissions/${id}/approve`, { method: 'POST' }),
    rejectSubmission: (id: string, reviewNotes?: string) =>
      request<{ status: string; reviewNotes?: string }>(`/api/marketplace/submissions/${id}/reject`, {
        method: 'POST', body: JSON.stringify({ reviewNotes }),
      }),
    install: (id: string) =>
      request<{ installed: boolean; installedId?: string; installCount: number }>(
        `/api/marketplace/listings/${id}/install`,
        { method: 'POST' },
      ),
  },
  push: {
    registerDevice: (token: string, platform: 'ios' | 'android' | 'web') =>
      request<{ id: string; platform: string }>('/api/push/devices', {
        method: 'POST',
        body: JSON.stringify({ token, platform }),
      }),
    unregisterDevice: (token: string) =>
      request<void>(`/api/push/devices/${encodeURIComponent(token)}`, { method: 'DELETE' }),
    test: () =>
      request<{ sent: boolean; deviceId: string; platform: string }>('/api/push/test', {
        method: 'POST',
      }),
  },
  // Iter-32 DESK-007 / INT-007 — PACS bridge (DICOMweb proxy + plugins).
  pacs: {
    searchStudies: (params: { accession?: string; patientId?: string; modality?: string; limit?: number }) => {
      const sp = new URLSearchParams();
      if (params.accession) sp.set('accession', params.accession);
      if (params.patientId) sp.set('patientId', params.patientId);
      if (params.modality) sp.set('modality', params.modality);
      if (params.limit) sp.set('limit', String(params.limit));
      return request<{ configured: boolean; upstreamStatus?: number; studies: unknown }>(
        `/api/pacs/studies?${sp.toString()}`,
      );
    },
    health: () =>
      request<{
        dicomWeb: { configured: boolean; reachable: boolean };
        orthanc: { configured: boolean; reachable: boolean; url?: string | null };
      }>('/api/pacs/health'),
    // Plugin list / toggle is desktop-side only — invoked through Tauri's
    // `pacs_plugins_list` / `pacs_plugins_set_enabled` commands. The web
    // surface degrades gracefully when not running inside the desktop
    // shell (returns []).
    plugins: async (): Promise<Array<{
      id: string; name: string; vendor: string; version: string;
      capabilities: string[]; enabled: boolean; verified: boolean; error?: string | null;
    }>> => {
      if (typeof window === 'undefined') return [];
      const tauri = (window as { __TAURI__?: { core?: { invoke?: (cmd: string) => Promise<unknown> }; invoke?: (cmd: string) => Promise<unknown> } }).__TAURI__;
      const invoke = tauri?.core?.invoke ?? tauri?.invoke;
      if (!invoke) return [];
      try {
        return await invoke('pacs_plugins_list') as Array<{
          id: string; name: string; vendor: string; version: string;
          capabilities: string[]; enabled: boolean; verified: boolean; error?: string | null;
        }>;
      } catch {
        return [];
      }
    },
    setPluginEnabled: async (pluginId: string, enabled: boolean): Promise<boolean> => {
      if (typeof window === 'undefined') return false;
      const tauri = (window as { __TAURI__?: { core?: { invoke?: (cmd: string, args: unknown) => Promise<unknown> }; invoke?: (cmd: string, args: unknown) => Promise<unknown> } }).__TAURI__;
      const invoke = tauri?.core?.invoke ?? tauri?.invoke;
      if (!invoke) return false;
      try {
        return await invoke('pacs_plugins_set_enabled', { pluginId, enabled }) as boolean;
      } catch {
        return false;
      }
    },
  },
  // Iter-32 INT-010 — SIEM push status.
  siem: {
    status: () =>
      request<{
        sinks: Array<{
          name: string;
          configured: boolean;
          lastPushAt: string | null;
          lastError: string | null;
          totalPushed: number;
          totalErrors: number;
        }>;
      }>('/api/siem/status'),
  },
  // Iter-35 PERF-004 — admin observability surface (synthetic
  // availability monitor). RBAC: ItAdmin / ComplianceReviewer.
  admin: {
    observability: {
      availability: () =>
        request<{
          windowSec: number;
          totalProbes: number;
          errorCount: number;
          errorRate: number;
          lastCheckedAt: string | null;
          targets: string[];
        }>('/api/admin/observability/availability'),
    },
  },
  usage: {
    summary: (params?: { from?: string; to?: string }) => {
      const q = new URLSearchParams();
      if (params?.from) q.set('from', params.from);
      if (params?.to) q.set('to', params.to);
      const qs = q.toString();
      return request<UsageSummary>(`/api/usage/summary${qs ? `?${qs}` : ''}`);
    },
  },
  analytics: {
    summary: (params?: { from?: string; to?: string; period?: string }) => {
      const q = new URLSearchParams();
      if (params?.from) q.set('from', params.from);
      if (params?.to) q.set('to', params.to);
      if (params?.period) q.set('period', params.period);
      const qs = q.toString();
      return request<{
        window: { from: string; to: string };
        product: {
          draftAcceptanceRate: number;
          impressionAcceptanceRate: number;
          timeSavedPerReport: number;
          validationPassRate: number;
          contradictionDetectionRate: number;
          editDistance: number;
          activeRadiologists: number;
          rulebookAdoption: number;
          providerCostPerReport: number;
          turnaroundTimeImpact: number;
          avgQualityScore: number | null;
        };
        governance: {
          unapprovedPromptUsage: number;
          phiViolationsBlocked: number;
          rulebookRegressionFailures: number;
          modelDriftAlerts: number;
          auditCompleteness: number;
        };
        ai: {
          totalRequests: number;
          okCount: number;
          blockedCount: number;
          errorCount: number;
          inputTokens: number;
          outputTokens: number;
          avgLatencyMs: number;
          costTotalUsd: number;
          byProvider: Array<{
            provider: string;
            adapter: string;
            requests: number;
            inputTokens: number;
            outputTokens: number;
            costInputUsd: number;
            costOutputUsd: number;
            costTotalUsd: number;
            unpriced: boolean;
          }>;
        };
      }>(`/api/analytics/summary${qs ? `?${qs}` : ''}`);
    },
    qualityTrends: (params?: { from?: string; to?: string; groupBy?: 'day' | 'week' }) => {
      const q = new URLSearchParams();
      if (params?.from) q.set('from', params.from);
      if (params?.to) q.set('to', params.to);
      if (params?.groupBy) q.set('groupBy', params.groupBy);
      const qs = q.toString();
      return request<QualityTrendsResponse>(`/api/analytics/quality-trends${qs ? `?${qs}` : ''}`);
    },
  },
  terminology: {
    radlexSearch: async (q: string, take = 20): Promise<RadLexHit[]> => {
      const sp = new URLSearchParams();
      sp.set('q', q);
      sp.set('take', String(take));
      const wire = await request<RadLexHitWire[]>(
        `/api/terminology/radlex/search?${sp.toString()}`,
      );
      return wire.map((w) => ({
        code: w.rid,
        preferredName: w.preferredLabel,
        synonyms: w.synonyms ?? [],
        definition: null,
      }));
    },
    rads: async (system: string): Promise<RadsEntry[]> => {
      const sys = await request<RadsSystemWire>(
        `/api/terminology/rads?system=${encodeURIComponent(system)}`,
      );
      return (sys.categories ?? []).map((c) => ({
        system: sys.system,
        code: c.code,
        label: c.shortLabel,
        description: sys.description ?? null,
      }));
    },
  },
  fhir: {
    importDiagnosticReport: (json: string) =>
      request<FhirImportResult & { deduplicated?: boolean }>(
        '/api/reports/import/fhir',
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/fhir+json' },
          body: json,
        },
      ),
  },
  mcp: {
    list: () => request<McpToolRow[]>('/api/mcp/tools'),
    register: (body: {
      name: string;
      version?: string;
      kind?: number;
      scope?: number;
      scopeString?: string;
      isBuiltIn?: boolean;
      manifestJson?: string;
      manifestSig?: string;
      allowedConnectorPaths?: string[];
    }) =>
      request<McpToolRow>('/api/mcp/tools', { method: 'POST', body: JSON.stringify(body) }),
    approve: (id: string) =>
      request<McpToolRow>(`/api/mcp/tools/${id}/approve`, { method: 'POST' }),
    block: (id: string, reason?: string) =>
      request<McpToolRow>(`/api/mcp/tools/${id}/block`, {
        method: 'POST',
        body: JSON.stringify({ reason: reason ?? 'manual' }),
      }),
    delete: (id: string) =>
      request<void>(`/api/mcp/tools/${id}`, { method: 'DELETE' }),
    test: (id: string, inputJson: string) =>
      request<{ status: string; output: string; latencyMs: number; memoryBytes: number }>(
        `/api/mcp/tools/${id}/test`,
        { method: 'POST', body: JSON.stringify({ inputJson }) },
      ),
  },
};

/** PRD §16.4 — Prompt Studio types. */
export type GoldenCaseResult = {
  caseName: string;
  passed: boolean;
  expectedRules: string[];
  actualRules: string[];
  qualityScore: number;
};

export type PromptOverrideVersion = {
  version: number;
  body: string;
  status: 'Draft' | 'Approved';
  updatedAt: string;
  updatedBy: string | null;
};

export type PromptVersionDiff = {
  v1: number;
  v2: number;
  oldBody: string;
  newBody: string;
};

export const PLAN_LABELS: Record<number, string> = {
  0: 'Trial',
  1: 'Team',
  2: 'Enterprise',
};

export const COMPLIANCE_LABELS: Record<number, string> = {
  0: 'Blocked',
  1: 'Sandbox',
  2: 'De-identified only',
  3: 'PHI-approved',
  4: 'Local only',
};
