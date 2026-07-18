/**
 * Tiny typed API client used by every Next.js page in the RadioPad
 * reporting workspace. The base URL is read from `NEXT_PUBLIC_API_BASE`
 * (set by Tauri / Capacitor builds) and falls back to the dev proxy
 * configured in `next.config.ts`.
 */

/** On-device STT engine mode selectable from the dictation overlay. */
export type SttMode = 'auto' | 'single' | 'ensemble';

/** One reconciled word from the multi-engine ensemble. `flagged` spans are the
 *  disagreement / safety-critical tokens the radiologist must eye-confirm. */
export type SttSpan = {
  text: string;
  flagged: boolean;
  reason: string | null;
  source: string;
};

/** One AI correction from the cross-check pass (original→corrected + provenance). */
export type CrossCheckCorrection = {
  id: string;
  sectionKey?: string | null;
  originalText: string;
  correctedText: string;
  startOffset: number;
  endOffset: number;
  reason: string;
  category: string;
  source: string;
  confidence: number;
  severity: 'safety' | 'warning' | 'info';
};

/** Report section keys a dictation draft can populate. */
export type DictationSectionKey =
  | 'indication' | 'technique' | 'findings' | 'impression' | 'recommendations';

/**
 * Result of the SAFETY-CHECKED dictation→report draft (dictation-engine brief §4.2):
 * §5.2 deterministic pass-through → formatter → §5.3 validation-diff → §5.6 sentinel.
 * `usedFallback` means the formatter output was REJECTED (§5.3) and `sections` holds the
 * dictionary-corrected transcript instead. `sentinelWarnings` require eye-confirmation (§5.6).
 */
export type DictationDraftResult = {
  sections: Partial<Record<DictationSectionKey, string>>;
  accepted: boolean;
  usedFallback: boolean;
  requiresReview: boolean;
  violations: { reason: string; detail: string }[];
  sentinelWarnings: { kind: string; detail: string }[];
  provider: string;
  model: string;
  latencyMs: number;
};

/** Desktop-created companion session (returned to the desktop host). */
export type CompanionSessionInit = {
  sessionId: string;
  pairingCode: string;
  expiresAt: string;
  /** Relative WebSocket path for the relay, e.g. `/ws/companion`. */
  wsUrl: string;
  /**
   * Short-lived (2h) bearer the desktop embeds in its pairing QR. The phone
   * scans the QR, adopts this as its auth token, and pairs — so there is NO
   * separate phone login. Authenticates as `tenantSlug`/`userEmail` only and is
   * revoked when the session ends. Absent on older backends (pre-QR-login).
   */
  companionToken?: string;
  tenantSlug?: string;
  userEmail?: string;
};

/** Result of the phone pairing to a desktop session by code. */
export type CompanionPairResult = {
  sessionId: string;
  hostDeviceName: string;
};

/** Companion session status snapshot. */
export type CompanionSessionInfo = {
  sessionId: string;
  status: string;
  hostDeviceName: string;
  companionDeviceName: string | null;
};

/** Poll result for an async cross-check job. */
export type CrossCheckStatus = {
  jobId: string;
  status: 'queued' | 'running' | 'completed' | 'failed';
  stage: string;
  error?: string | null;
  transcript?: string | null;
  engineIds?: string | null;
  latencyMs?: number | null;
  corrections?: CrossCheckCorrection[] | null;
};

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

/**
 * Base URL of the CLOUD companion relay. The desktop↔phone companion channel
 * must meet on a host BOTH devices can reach — never the desktop's local
 * `127.0.0.1` sidecar. Resolution order:
 *   1. `NEXT_PUBLIC_COMPANION_BASE` (explicit override).
 *   2. `NEXT_PUBLIC_API_BASE` when it is a remote http(s) origin (the phone and
 *      cloud-backed desktops already point here).
 *   3. The known production relay host (matches the desktop CSP allow-list).
 * Companion REST + WebSocket both use this so the session the desktop creates is
 * the one the phone can pair to. NOTE: the caller's bearer must be valid on the
 * cloud relay — a purely local-only desktop needs cloud connectivity for
 * companion mode (see the surface-companion design note).
 */
const DEFAULT_COMPANION_BASE = 'https://radiopadstudio.com';
let resolvedCompanionBase: string | null = null;
export function companionBase(): string {
  if (resolvedCompanionBase !== null) return resolvedCompanionBase;
  const override = (publicEnv('NEXT_PUBLIC_COMPANION_BASE') || '').replace(/\/+$/, '');
  if (override) return (resolvedCompanionBase = override);
  if (CONFIGURED_API_BASE && /^https?:\/\//i.test(CONFIGURED_API_BASE)) {
    return (resolvedCompanionBase = CONFIGURED_API_BASE);
  }
  return (resolvedCompanionBase = DEFAULT_COMPANION_BASE);
}

/**
 * Override the resolved companion relay base at runtime. The phone calls this
 * with the `base` carried in the pairing QR so its `pair` REST call and the WS
 * relay both address the exact cloud host the desktop advertised — rather than
 * relying on a build-time default. Passing an empty value re-arms auto-resolution.
 */
export function setCompanionBase(base: string): void {
  const cleaned = (base || '').replace(/\/+$/, '');
  resolvedCompanionBase = cleaned || null;
}

/** The companion relay as a `ws(s)://…` origin (for the raw WebSocket client). */
export function companionWsBase(): string {
  return companionBase().replace(/^http/i, 'ws');
}

/**
 * Base URL of the bundled on-device STT sidecar, resolved once via the desktop
 * `get_local_stt_url` command. Empty string on web / non-desktop builds (no
 * sidecar). Dictation transcription is routed here so PHI audio is transcribed
 * locally and never leaves the machine; every other call uses `apiBase()`
 * (production). See desktop `sidecar_manager` / `get_local_stt_url`.
 */
let resolvedLocalSttBase: string | null = null;
export async function localSttBase(): Promise<string> {
  if (resolvedLocalSttBase !== null) return resolvedLocalSttBase;
  if (typeof window !== 'undefined') {
    const tauri = (window as typeof window & {
      __TAURI__?: {
        core?: { invoke?: (cmd: string) => Promise<unknown> };
        invoke?: (cmd: string) => Promise<unknown>;
      };
    }).__TAURI__;
    const invoke = tauri?.core?.invoke ?? tauri?.invoke;
    try {
      const url = await invoke?.('get_local_stt_url');
      if (typeof url === 'string' && url.length > 0) {
        resolvedLocalSttBase = url.replace(/\/$/, '');
        return resolvedLocalSttBase;
      }
    } catch {
      /* not the desktop shell — fall through to web/cloud path */
    }
  }
  resolvedLocalSttBase = '';
  return resolvedLocalSttBase;
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

/**
 * Map a raw network failure (fetch's TypeError "Failed to fetch") to a message
 * a radiologist can act on. The raw text leaked into UI banners during the
 * 2026-07-12 incident (proxy killed a long AI call mid-flight).
 *
 * `status` deliberately stays undefined: useAuthSession and usePermissions
 * classify `status === undefined` as "signed out / unreachable" and stamping a
 * number here flips AuthGate/PermissionGate into a misleading
 * "no permission" state on any network blip. Transport-level detection keys
 * on `kind === 'network'` instead (used by the AI job poll loop).
 */
function friendlyNetworkError(e: unknown): Error {
  return Object.assign(
    new Error('Could not reach the RadioPad server — the connection dropped or the request timed out. Check your connection and try again.'),
    { kind: 'network', cause: e },
  );
}

async function fetchWithAuthRetry(
  url: string,
  init: RequestInit,
  headers: Headers,
): Promise<Response> {
  try {
    let res = await fetchOnce(url, init, headers);
    if ((res.status === 401 || res.status === 403) && !cachedAuthToken && await hydrateAuthTokenFromSecureStore()) {
      applyAuthHeader(headers);
      res = await fetchOnce(url, init, headers);
    }
    return res;
  } catch (e) {
    if (e instanceof TypeError) throw friendlyNetworkError(e);
    throw e;
  }
}

/**
 * Extract a non-OK response's body for the thrown API error. The body stream
 * can only be consumed ONCE, so read it as text first and then attempt JSON —
 * `res.json()` followed by a fallback `res.text()` throws
 * "body stream already read" and masks the real server error (seen when a
 * not-yet-deployed endpoint returned an empty-body 404).
 */
async function errorBody(res: Response): Promise<unknown> {
  let raw = '';
  try {
    raw = await res.text();
  } catch {
    return null;
  }
  if (!raw) return null;
  try {
    return JSON.parse(raw);
  } catch {
    return raw;
  }
}

/**
 * Build the error thrown for a non-OK response. Backend error payloads carry a
 * `kind` discriminator (e.g. `ubag_unconfigured`, `target_not_allowed`, and the
 * GlobalExceptionMiddleware 502 kinds `provider` / `transport`); surface it as
 * a top-level `kind` so callers can branch without digging into `body`.
 */
async function apiError(res: Response): Promise<Error> {
  const body = await errorBody(res);
  const kind =
    body && typeof body === 'object' && typeof (body as { kind?: unknown }).kind === 'string'
      ? (body as { kind: string }).kind
      : undefined;
  return Object.assign(new Error(`API ${res.status} ${res.statusText}`), { status: res.status, body, kind });
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
    throw await apiError(res);
  }
  if (res.status === 204) return undefined as unknown as T;
  const ct = res.headers.get('content-type') || '';
  if (ct.includes('application/json') || ct.includes('application/fhir+json')) {
    return (await res.json()) as T;
  }
  return (await res.text()) as unknown as T;
}

/** Envelope returned by GET /api/reports/{id}/ai/jobs/{jobId}. */
type AiJobEnvelope<T> = {
  jobId: string;
  kind: 'ai' | 'generate';
  mode: string;
  status: 'running' | 'ok' | 'error';
  elapsedMs: number;
  result: T | null;
  error: string | null;
  errorKind: string | null;
};

const AI_JOB_POLL_MS = 2_000;
const AI_JOB_FIRST_POLL_MS = 300;
const AI_JOB_MAX_WAIT_MS = 10 * 60_000;

/** Poll-side statuses worth retrying: the GET is idempotent and the job keeps
 * running server-side, so a proxy reload 502/503/504, a rate-limit 429/408, or
 * a transient 500 (SQLite busy under the detached job's write) must not abandon
 * a multi-minute generation. Deterministic failures (401/403, 404
 * job_not_found after a server restart) fail fast. */
function isTransientPollError(e: unknown): boolean {
  const { status, kind } = e as { status?: number; kind?: string };
  if (kind === 'network') return true;
  return status === 408 || status === 429 || status === 500 || status === 502 || status === 503 || status === 504;
}

/**
 * Submit an async report-AI job and poll it to a terminal state. Each poll is
 * a fast GET, so no proxy or webview timeout can kill the generation; transient
 * poll failures are retried until the overall deadline (the job keeps running
 * server-side). Terminal errors are re-thrown in the same `{status, body}`
 * shape `request()` produces, so existing catch blocks
 * (`e.body?.error ?? e.message`) render them unchanged. The poll interval
 * ramps 300ms → 2s so fast completions (policy rejections, cached/API-backed
 * providers) don't pay a fixed 2s tax.
 */
async function runReportAiJob<T>(reportId: string, submitPath: string, body: unknown): Promise<T> {
  const submitted = await request<{ jobId: string }>(submitPath, {
    method: 'POST',
    body: JSON.stringify(body),
  });
  const started = Date.now();
  let waitMs = AI_JOB_FIRST_POLL_MS;
  for (;;) {
    await new Promise((r) => setTimeout(r, waitMs));
    waitMs = Math.min(waitMs * 2, AI_JOB_POLL_MS);
    let s: AiJobEnvelope<T>;
    try {
      s = await request<AiJobEnvelope<T>>(`/api/reports/${reportId}/ai/jobs/${submitted.jobId}`);
    } catch (e) {
      if (isTransientPollError(e) && Date.now() - started < AI_JOB_MAX_WAIT_MS) continue;
      throw e;
    }
    if (s.status === 'ok' && s.result != null) return s.result;
    if (s.status === 'error') {
      throw Object.assign(new Error(s.error || 'AI generation failed.'), {
        status: 502,
        kind: s.errorKind || 'ai_job_failed',
        body: { error: s.error || 'AI generation failed.', kind: s.errorKind || 'ai_job_failed' },
      });
    }
    if (Date.now() - started > AI_JOB_MAX_WAIT_MS) {
      // Only the generate kind persists its result server-side; a runAi result
      // lives in the job payload and is lost once this loop gives up — never
      // promise it will show up on the report.
      const message = submitPath.includes('/generate/')
        ? 'The generation is taking unusually long. It may still finish in the background — reopen the report in a minute, or try again.'
        : 'The AI request is taking unusually long and the wait was abandoned. Please try again.';
      throw Object.assign(new Error(message), {
        status: 504,
        body: { error: message, kind: 'timeout' },
      });
    }
  }
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
    throw await apiError(res);
  }
  return await res.blob();
}

async function requestForm<T>(path: string, form: FormData, signal?: AbortSignal): Promise<T> {
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
    signal,
  }, headers);
  if (!res.ok) {
    throw await apiError(res);
  }
  const ct = res.headers.get('content-type') || '';
  if (ct.includes('application/json')) return (await res.json()) as T;
  return (await res.text()) as unknown as T;
}

/**
 * Multipart upload to an explicit base URL (the on-device STT sidecar), with NO
 * tenant/auth headers and no credentials. The local sidecar's `/api/stt/...`
 * endpoint is anonymous and loopback-bound, so the production session token is
 * neither needed nor appropriate to send to localhost.
 */
async function requestFormTo<T>(base: string, path: string, form: FormData, signal?: AbortSignal): Promise<T> {
  const res = await fetch(`${base}${path}`, { method: 'POST', body: form, credentials: 'omit', signal });
  if (!res.ok) {
    throw await apiError(res);
  }
  const ct = res.headers.get('content-type') || '';
  if (ct.includes('application/json')) return (await res.json()) as T;
  return (await res.text()) as unknown as T;
}

/**
 * JSON request to an explicit base URL — used by the on-device model manager,
 * which must talk to the bundled STT sidecar (loopback, anonymous) rather than
 * the production API. No tenant/auth headers and credentials omitted, like
 * `requestFormTo` (the sidecar is loopback-bound and unauthenticated).
 */
async function requestTo<T>(base: string, path: string, init?: RequestInit): Promise<T> {
  const headers = new Headers(init?.headers || {});
  if (init?.body && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json');
  const res = await fetch(`${base}${path}`, { ...init, headers, credentials: 'omit' });
  if (!res.ok) {
    throw await apiError(res);
  }
  if (res.status === 204) return undefined as unknown as T;
  const ct = res.headers.get('content-type') || '';
  if (ct.includes('application/json')) return (await res.json()) as T;
  return (await res.text()) as unknown as T;
}

/**
 * Like `request`, but targets the cloud companion relay (`companionBase()`)
 * while still carrying the caller's auth + tenant headers — the companion
 * endpoints are tenant-scoped. Used only by `api.companion.*`.
 */
async function requestCompanion<T>(path: string, init?: RequestInit): Promise<T> {
  const headers = new Headers(init?.headers || {});
  if (!headers.has('Content-Type')) headers.set('Content-Type', 'application/json');
  applyTenantHeaders(headers);
  applyAuthHeader(headers);
  const base = companionBase();
  const res = await fetch(`${base}${normalizeRequestPath(path, init)}`, {
    ...init,
    headers,
    credentials: base ? 'include' : 'same-origin',
  });
  if (!res.ok) {
    throw await apiError(res);
  }
  if (res.status === 204) return undefined as unknown as T;
  const ct = res.headers.get('content-type') || '';
  if (ct.includes('application/json')) return (await res.json()) as T;
  return (await res.text()) as unknown as T;
}

/**
 * Route an on-device-model-manager call to the local STT sidecar when running in
 * the desktop shell (where the models actually live and RADIOPAD_LOCAL_STT_ENABLED
 * is set), else fall back to the hosted API — which returns `enabled:false`, so the
 * web shows the catalog read-only. Without this the desktop would hit production
 * (on-device STT disabled there) and every model would show "not downloaded" with
 * the actions disabled.
 */
async function requestLocal<T>(path: string, init?: RequestInit): Promise<T> {
  const base = await localSttBase();
  return base ? requestTo<T>(base, path, init) : request<T>(path, init);
}

/**
 * The on-device STT sidecar is not reachable. Cross Check deliberately has NO
 * cloud fallback — dictation audio is PHI and stays on-device — so callers get
 * the same "engine warming up / unavailable" shape the transcribe path uses
 * (503 + `stt_unavailable`) instead of a confusing 404 from a nonexistent
 * hosted endpoint.
 */
function sttUnavailableError(): Error {
  return Object.assign(
    new Error('On-device cross-check engine is unavailable — it runs only in the desktop app, and dictation audio never leaves this machine. Give the engine a moment and try again.'),
    { status: 503, kind: 'stt_unavailable', body: { kind: 'stt_unavailable' } },
  );
}

export type Report = {
  id: string;
  tenantId: string;
  status: 'Draft' | 'Validated' | 'Acknowledged' | 'Exported' | number;
  rulebookId: string | null;
  templateId: string | null;
  /** Manual-override pins — true when the radiologist explicitly selected the
   * binding. While pinned, study-context changes never auto-rebind it; PATCHing
   * the pin to false resets the binding to auto-resolution. */
  rulebookPinned: boolean;
  templatePinned: boolean;
  study: {
    accessionNumber: string;
    modality: string;
    bodyPart: string;
    /** Hybrid contrast model — "" | "None" | "With" | "WithAndWithout". Drives contrast-aware template resolution. */
    contrast: string;
    /** Iter-36 — patient age in years (null when unknown). Replaced study-context indication. */
    age: number | null;
    /** Iter-36 — patient gender (Male/Female/Other/Unknown). */
    gender: string;
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
  /** Hybrid contrast model — "" (agnostic) | "None" | "With" | "WithAndWithout". */
  contrast?: string;
  subspecialty: string;
  sectionsJson: string;
  updatedAt: string;
  /** Iter-34 GOV-001 — `TemplateStatus` enum: 0 Draft / 1 Approved / 2 Deprecated / 3 Review. */
  status?: number;
  /** Iter-34 GOV-001 — set when an admin approves the template. */
  approvedAt?: string | null;
};

/** Iter-36 — admin-managed catalog row (Modality or BodyPart), tenant-scoped. */
export type CatalogItem = {
  id: string;
  code: string;
  name: string;
  active: boolean;
  sortOrder: number;
  updatedAt?: string;
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

export type UbagAlert = {
  kind: 'login_lost' | 'failing';
  target: string;
  since: string;
  remedy: string;
};

export type UbagStatus = {
  health: UbagHealth;
  browser: UbagBrowserSummary;
  targets: UbagTarget[];
  allowedTargets: string[];
  orderedTargets: string[];
  alerts: UbagAlert[];
  gatewayUnreachableSince?: string | null;
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

/** Master-admin user row from GET /api/users. */
export type UserRow = {
  id: string;
  email: string;
  displayName: string;
  role: string;
  isActive: boolean;
  mfaEnabled: boolean;
  lockedUntil: string | null;
  locked: boolean;
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

/** On-device model manager — kinds, lifecycle, and per-model status. */
export type LocalModelKind = 'Stt' | 'Tts' | 'Orchestrator';
export type ProvisionState =
  | 'NotStarted'
  | 'Downloading'
  | 'Verifying'
  | 'Extracting'
  | 'Installing'
  | 'Ready'
  | 'Failed';

export type ModelProgress = {
  id: string;
  state: ProvisionState;
  bytesDownloaded: number;
  totalBytes: number;
  error: string | null;
};

/**
 * How a model entry is provisioned + run, driving the card's actions:
 * - `HostedFile` — we download/verify a model bundle (Parakeet).
 * - `WindowsBuiltIn` — System.Speech / SAPI: ships with Windows, no download.
 * - `WindowsLanguagePack` — WinRT speech: "download" opens Windows speech settings.
 * - `BrowserWebSpeech` — Edge Web Speech: runs in the WebView; availability probed
 *   in the frontend, not the sidecar.
 */
export type ModelProvisioning =
  | 'HostedFile'
  | 'WindowsBuiltIn'
  | 'WindowsLanguagePack'
  | 'BrowserWebSpeech';

export type LocalModel = {
  id: string;
  displayName: string;
  kind: LocalModelKind;
  engine: string;
  sizeBytes: number;
  license: string;
  /** Roadmap kinds (TTS / orchestrator) with no engine yet — render as "coming soon". */
  placeholder: boolean;
  /** How this entry is provisioned + run (defaults to HostedFile for older builds). */
  provisioning?: ModelProvisioning;
  /** Optional card note (e.g. the online/PHI warning on Edge / WinRT-online). */
  note?: string | null;
  downloaded: boolean;
  /** Engine loaded + usable right now (always false on a web build). */
  available: boolean;
  /** True when this is the selected primary dictation engine (STT only). */
  isPrimary: boolean;
  progress: ModelProgress;
};

/** `enabled` is false on a web/server build (no local engine) → the UI shows a desktop-only notice. */
export type LocalModelsResponse = { enabled: boolean; models: LocalModel[] };

export type ModelTestResult = {
  ok: boolean;
  engine: string;
  latencyMs: number;
  transcript: string | null;
  sampleSource: string;
  error?: string;
  /** Full exception text for IT hand-off (only on failure). */
  detail?: string;
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
    create: (
      body: Partial<Report['study']> & {
        // `indication` is the report-body section (not study context); the backend
        // create DTO accepts it alongside the study selection key.
        indication?: string;
        rulebookId?: string | null;
        templateId?: string | null;
      },
    ) => request<Report>('/api/reports', { method: 'POST', body: JSON.stringify(body) }),
    // Iter-36 — study-context fields (modality/bodyPart/age/gender) are patched as
    // flat top-level keys, matching the backend PatchReportDto; everything else is a
    // standard Partial<Report> body field.
    patch: (
      id: string,
      body: Partial<Report> & {
        modality?: string;
        bodyPart?: string;
        contrast?: string;
        age?: number | null;
        gender?: string;
      },
    ) => request<Report>(`/api/reports/${id}`, { method: 'PATCH', body: JSON.stringify(body) }),
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
    /**
     * AI section generation via the async job endpoints (submit + poll,
     * 2026-07-12). The old synchronous POST held the connection open for the
     * whole provider call — minutes on UBAG browser-driven targets — so any
     * proxy timeout or the webview's ~300s kill surfaced as "Failed to fetch"
     * AND cancelled the in-flight generation. Submit returns a job id
     * immediately; each poll is a fast request; a dropped poll no longer
     * cancels the run. Same return shape as the old call.
     */
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
      runReportAiJob<{ text: string; provider: string; model: string; latencyMs: number; promptVersion: string; mode: string }>(
        id,
        `/api/reports/${id}/ai/jobs`,
        body,
      ),
    /**
     * Whole-report generation for the guided intake flow (`/reports/new`). Runs the
     * structured generation prompt through the selected provider (or auto-routes when
     * `providerId` is omitted) and returns the report with every AI-populated section
     * filled and flagged `.ai-mark`. Empty sections keep the intake-seeded text.
     * Uses the async job endpoints — see `runAi`.
     */
    generate: (id: string, body: { providerId?: string } = {}) =>
      runReportAiJob<Report>(
        id,
        `/api/reports/${id}/generate/jobs`,
        body.providerId ? { providerId: body.providerId } : {},
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
    /**
     * F8 — one-command "Sign & Send": Primary sign-off → acknowledge (blocker-gated on the server)
     * → export, chaining the EXISTING gated endpoints in order. If sign or acknowledge fails (e.g.
     * validation blockers), it STOPS before export — the sign-off gate is never bypassed and nothing
     * is auto-signed (the radiologist explicitly triggers this one action).
     */
    signAndSend: async (
      id: string,
      opts: { note?: string; format?: 'fhir' | 'hl7' | 'json' | 'text' } = {},
    ) => {
      const signature = await api.reports.sign(id, { role: 'Primary', note: opts.note });
      const report = await api.reports.acknowledge(id);
      const format = opts.format ?? 'text';
      const exported =
        format === 'fhir' ? await api.reports.exportFhir(id)
        : format === 'json' ? await api.reports.exportJson(id)
        : format === 'hl7' ? await api.reports.exportHl7(id)
        : await api.reports.exportText(id);
      return { signature, report, exported, format };
    },
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
    /**
     * Dictation-engine brief §4.2 — the SAFETY-CHECKED dictation→report draft:
     * deterministic pass-through (§5.2) → formatter (PHI-gated AiGateway) →
     * validation-diff (§5.3, fail-safe fallback to the corrected transcript) →
     * laterality/negation/gender sentinel (§5.6) → local audit (§5.7). Report-scoped
     * cloud path today (like cleanupDictation); the optional local MedGemma formatter
     * is selected on the desktop once the on-device sidecar is wired.
     */
    dictationDraft: (id: string, rawDictation: string) =>
      request<DictationDraftResult>(`/api/reports/${id}/dictation/draft`, {
        method: 'POST',
        body: JSON.stringify({ rawDictation }),
      }),
    /**
     * Optional OFFLINE draft path (desktop only): runs the whole safety pipeline on the loopback
     * sidecar with the local MedGemma formatter — the transcript (PHI) never leaves the machine.
     * Stateless (report context is passed in the body, not resolved from the DB). Returns 503 until
     * the on-device formatter + bundled llama-server are provisioned; callers fall back to
     * `dictationDraft` (cloud) on failure.
     */
    dictationDraftLocal: async (
      raw: string,
      ctx: {
        modality?: string; bodyPart?: string; indication?: string; patientSex?: string;
        corrections?: { from: string; to: string }[];
      } = {},
    ): Promise<DictationDraftResult> => {
      const local = await localSttBase();
      if (!local) throw new Error('On-device formatter is not available on this surface.');
      const res = await fetch(`${local}/api/dictation/draft-local`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ rawDictation: raw, ...ctx }),
      });
      if (!res.ok) throw new Error(`On-device formatting failed (HTTP ${res.status}).`);
      return (await res.json()) as DictationDraftResult;
    },
    // Dictation transcription. On the desktop the recorded audio is transcribed
    // FULLY ON-DEVICE by the bundled STT sidecar (Parakeet, CPU) — the
    // PHI-bearing audio never leaves the machine; only the
    // resulting de-identified transcript is saved to the production report. On
    // web (no sidecar) it falls back to the report-scoped cloud path, where PHI
    // routing is handled by the provider router exactly like text dictation.
    transcribe: async (id: string, audio: Blob, mode?: SttMode, signal?: AbortSignal) => {
      const form = new FormData();
      // Desktop converts to 16 kHz mono WAV for the on-device engine; web sends
      // the original webm. Name the part by type so the backend content-type
      // check sees the right format (it keys off the blob's MIME type).
      const name = audio.type.includes('wav') ? 'dictation.wav' : 'dictation.webm';
      form.append('audio', audio, name);
      // Per-request engine mode (on-device picker). 'auto' uses the server default.
      if (mode && mode !== 'auto') form.append('mode', mode);
      type TranscribeResult = {
        transcript: string;
        provider: string;
        model: string;
        latencyMs: number;
        spans?: SttSpan[] | null;
      };
      const local = await localSttBase();
      if (local) {
        // Desktop: on-device, loopback-only, anonymous STT endpoint. Not
        // report-scoped — the engine needs only the audio. Deliberately NO cloud
        // fallback here: if the on-device engine isn't ready it surfaces an error
        // rather than silently shipping PHI audio off-device.
        return requestFormTo<TranscribeResult>(local, '/api/stt/transcribe', form, signal);
      }
      return requestForm<TranscribeResult>(`/api/reports/${id}/dictation/transcribe`, form, signal);
    },
    /**
     * Manual "Cross Check": re-run the retained dictation audio through the extra
     * on-device engines, reconcile against the live draft, and (later) an LLM
     * medical pass. Async — returns a job id to poll via `crossCheckStatus`.
     * Only the loopback sidecar handles the audio: there is deliberately NO
     * cloud fallback — dictation audio is PHI and never leaves the machine. If
     * the sidecar isn't available (web surface, or engine still warming up) the
     * caller surfaces the engine-unavailable state.
     */
    crossCheck: async (
      _id: string,
      audio: Blob,
      opts: { liveTranscript: string; sectionKey?: string; useUbag?: boolean },
    ): Promise<{ jobId: string }> => {
      const form = new FormData();
      const name = audio.type.includes('wav') ? 'dictation.wav' : 'dictation.webm';
      form.append('audio', audio, name);
      form.append('liveTranscript', opts.liveTranscript);
      if (opts.sectionKey) form.append('sectionKey', opts.sectionKey);
      if (opts.useUbag) form.append('useUbag', 'true');
      const local = await localSttBase();
      if (!local) throw sttUnavailableError();
      return requestFormTo<{ jobId: string }>(local, '/api/stt/crosscheck', form);
    },
    crossCheckStatus: async (_id: string, jobId: string): Promise<CrossCheckStatus> => {
      const local = await localSttBase();
      if (!local) throw sttUnavailableError();
      return requestTo<CrossCheckStatus>(local, `/api/stt/crosscheck/${jobId}`);
    },
    /**
     * LLM medical-accuracy review of already-transcribed text. Always hosted (it
     * needs the tenant + AI gateway); opt-in `useUbag` routes via the cloud gateway.
     */
    crossCheckReview: async (
      id: string,
      body: { text: string; sectionKey?: string; useUbag?: boolean },
    ): Promise<{ provider: string; model: string; latencyMs: number; corrections: CrossCheckCorrection[] }> =>
      request(`/api/reports/${id}/crosscheck/review`, {
        method: 'POST',
        body: JSON.stringify(body),
      }),
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
  /**
   * On-device AI model manager. On the desktop these calls go to the bundled STT
   * sidecar (via `localSttBase()` → `requestLocal`), where the models are actually
   * downloaded/run and `enabled` is true. On the web there is no sidecar, so they
   * fall back to the hosted API which returns `enabled:false` and the page renders
   * the catalog read-only.
   */
  localModels: {
    list: () => requestLocal<LocalModelsResponse>('/api/local-models'),
    download: (id: string) =>
      requestLocal<{ id: string; state: ProvisionState; alreadyInstalled?: boolean; startedUtc?: string }>(
        `/api/local-models/${encodeURIComponent(id)}/download`,
        { method: 'POST' },
      ),
    progress: (id: string) =>
      requestLocal<ModelProgress>(`/api/local-models/${encodeURIComponent(id)}/progress`),
    remove: (id: string) =>
      requestLocal<{ id: string; deleted: boolean }>(`/api/local-models/${encodeURIComponent(id)}`, {
        method: 'DELETE',
      }),
    test: (id: string) =>
      requestLocal<ModelTestResult>(`/api/local-models/${encodeURIComponent(id)}/test`, { method: 'POST' }),
    diagnostics: (id: string) =>
      requestLocal<unknown>(`/api/local-models/${encodeURIComponent(id)}/diagnostics`),
    setPrimary: (id: string) =>
      requestLocal<{ id: string; isPrimary: boolean }>(
        `/api/local-models/${encodeURIComponent(id)}/primary`,
        { method: 'POST' },
      ),
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
    /**
     * Non-destructive dry-run: validate sample findings against a rulebook
     * without persisting (or touching) any real report. `rulebookId` is the
     * rulebook row's GUID. See `PromptStudioController.Validate`.
     */
    testValidation: (body: { rulebookId: string; findings: string; promptOverrideId?: string | null }) =>
      request<ValidationResult>('/api/prompts/validate', {
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
      contrast?: string;
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
   * Iter-36 — admin-managed imaging-modality catalog (tenant-scoped). Reads are
   * open; save/remove require `modalities.manage`.
   */
  modalities: {
    list: () => request<CatalogItem[]>('/api/modalities'),
    save: (body: { id?: string; code: string; name?: string; active?: boolean; sortOrder?: number }) =>
      request<CatalogItem>('/api/modalities', { method: 'POST', body: JSON.stringify(body) }),
    remove: (id: string) =>
      request<void>(`/api/modalities/${id}`, { method: 'DELETE' }),
  },
  /** Iter-36 — admin-managed body-part catalog (tenant-scoped). Mirrors `modalities`. */
  bodyParts: {
    list: () => request<CatalogItem[]>('/api/body-parts'),
    save: (body: { id?: string; code: string; name?: string; active?: boolean; sortOrder?: number }) =>
      request<CatalogItem>('/api/body-parts', { method: 'POST', body: JSON.stringify(body) }),
    remove: (id: string) =>
      request<void>(`/api/body-parts/${id}`, { method: 'DELETE' }),
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
        throw await apiError(res);
      }
      return (await res.json()) as { upserts: number; removed: number };
    },
  },
  /**
   * F7 (dictation brief §6) — the signed-in radiologist's personal correction dictionary.
   * Deterministic find→replace applied BEFORE the LLM and layered over the org lexicon (the
   * user's entry wins for the same term). Backend: `UserCorrectionsController`, upserts on `from`.
   */
  userCorrections: {
    list: () => request<{ id: string; from: string; to: string }[]>('/api/user-corrections'),
    /** Add or update a personal correction (idempotent on the source term). */
    save: (body: { from: string; to: string }) =>
      request<{ id: string; from: string; to: string }>('/api/user-corrections', {
        method: 'POST',
        body: JSON.stringify(body),
      }),
    delete: (id: string) =>
      request<void>(`/api/user-corrections/${id}`, { method: 'DELETE' }),
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
    /** Master-admin user management (RBAC: UsersManage). Tenant-scoped. */
    list: () => request<UserRow[]>('/api/users'),
    roles: () => request<{ roles: Array<{ value: string; permissions: number }> }>('/api/users/roles'),
    create: (body: { email: string; displayName?: string; role: string; tempPassword?: string }) =>
      request<{ id: string; email: string; displayName: string; role: string; tempPassword: string }>(
        '/api/users',
        { method: 'POST', body: JSON.stringify(body) },
      ),
    update: (id: string, body: { displayName?: string; role?: string; isActive?: boolean }) =>
      request<{ id: string; changed: boolean; fields?: string[] }>(`/api/users/${id}`, {
        method: 'PATCH',
        body: JSON.stringify(body),
      }),
    remove: (id: string) =>
      request<{ id: string; deactivated: boolean }>(`/api/users/${id}`, { method: 'DELETE' }),
    lockout: (id: string) =>
      request<{ id: string; isActive: boolean; changed: boolean }>(`/api/users/${id}/lockout`, { method: 'POST' }),
    unlock: (id: string) =>
      request<{ id: string; isActive: boolean; changed: boolean }>(`/api/users/${id}/unlock`, { method: 'POST' }),
    revokeSessions: (id: string) =>
      request<{ id: string; sessionEpoch: number }>(`/api/users/${id}/revoke-sessions`, { method: 'POST' }),
    resetPassword: (id: string, tempPassword?: string) =>
      request<{ id: string; tempPassword: string }>(`/api/users/${id}/reset-password`, {
        method: 'POST',
        body: JSON.stringify({ tempPassword }),
      }),
    resetMfa: (id: string) =>
      request<{ id: string; mfaEnabled: boolean }>(`/api/users/${id}/reset-mfa`, { method: 'POST' }),
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
  registration: {
    /**
     * Self-serve SaaS onboarding — creates a brand-new tenant (organization) and
     * its first admin user, then emails a passwordless setup link. The admin
     * finishes via the existing magic-link consume flow. `devLink` is returned
     * only in non-production responses. Surfaces typed `kind`s on the error body:
     * `signup_disabled` (403), `validation` (400), `slug_taken` (409),
     * `rate-limit` (429), `email_unavailable` (503).
     */
    createOrganization: (body: {
      organizationName: string;
      slug?: string;
      adminEmail: string;
      adminName?: string;
      callbackUrl?: string;
    }) =>
      request<{ ok: boolean; slug: string; devLink?: string }>(
        '/api/registration/create-organization',
        { method: 'POST', body: JSON.stringify(body) },
      ),
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
      request<{ token?: string; tenant: string; user: string; expiresAt?: string; mfaRequired?: boolean }>(
        '/api/auth/signin',
        { method: 'POST', body: JSON.stringify({ tenant, user }) },
      ),
    /**
     * AUTH-001 — primary password sign-in. Returns one of:
     *  - `{ mfaRequired }`        → finish via `mfaLogin` (enrolled user)
     *  - `{ mfaSetupRequired, setupToken }` → forced first-login TOTP enrolment
     *    via `mfaEnroll(setupToken)` + `mfaVerify(setupToken)` (which mints the
     *    session). No session token is ever returned by this call directly.
     */
    passwordSignIn: (tenant: string, email: string, password: string) =>
      request<{
        tenant: string;
        user: string;
        mfaRequired?: boolean;
        mfaSetupRequired?: boolean;
        setupToken?: string;
      }>('/api/auth/password', {
        method: 'POST',
        body: JSON.stringify({ tenant, email, password }),
      }),
    /** Self-service password change for the signed-in user. */
    passwordChange: (currentPassword: string, newPassword: string) =>
      request<{ ok: boolean }>('/api/auth/password/change', {
        method: 'POST',
        body: JSON.stringify({ currentPassword, newPassword }),
      }),
    /** Email-free password reset: prove possession of the enrolled TOTP. */
    passwordResetWithTotp: (tenant: string, email: string, code: string, newPassword: string) =>
      request<{ ok: boolean }>('/api/auth/password/reset-with-totp', {
        method: 'POST',
        body: JSON.stringify({ tenant, email, code, newPassword }),
      }),
    // Step-up: exchange a verified 6-digit TOTP code for a session token after a
    // single-factor sign-in returned { mfaRequired: true }.
    mfaLogin: (tenant: string, email: string, code: string) =>
      request<{ token: string; tenant: string; user: string; expiresAt: string }>(
        '/api/auth/mfa/login',
        { method: 'POST', body: JSON.stringify({ tenant, email, code }) },
      ),
    /** Whether the account already has a confirmed authenticator-app (TOTP)
     *  enrolment. Used by the Settings security page to render persisted state. */
    mfaStatus: (tenant: string, email: string) =>
      request<{ mfaEnabled: boolean }>(
        `/api/auth/mfa/status?tenant=${encodeURIComponent(tenant)}&email=${encodeURIComponent(email)}`,
      ),
    /** TOTP enrolment. `setupToken` authorizes the forced first-login path when
     *  the user has no session yet (otherwise the request identity is used). */
    mfaEnroll: (tenant: string, email: string, setupToken?: string) =>
      request<{ secret: string; otpauth: string }>('/api/auth/mfa/enroll', {
        method: 'POST',
        body: JSON.stringify({ tenant, email, setupToken }),
      }),
    /** Confirm TOTP enrolment. With a `setupToken` (forced first-login) a session
     *  token is minted and returned; otherwise just `{ ok, mfaEnabled }`. */
    mfaVerify: (tenant: string, email: string, code: string, setupToken?: string) =>
      request<{ ok: boolean; mfaEnabled: boolean; token?: string; tenant?: string; user?: string; expiresAt?: string }>(
        '/api/auth/mfa/verify',
        { method: 'POST', body: JSON.stringify({ tenant, email, code, setupToken }) },
      ),
    magicLinkRequest: (tenant: string, email: string, callbackUrl?: string) =>
      request<{ ok: boolean; devLink?: string }>('/api/auth/magic-link/request', {
        method: 'POST',
        body: JSON.stringify({ tenant, email, callbackUrl }),
      }),
    magicLinkConsume: (token: string) =>
      request<{ token?: string; tenant: string; user: string; expiresAt?: string; mfaRequired?: boolean }>(
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
    webAuthnDeleteCredential: (id: string) =>
      request<void>(`/api/auth/webauthn/credentials/${encodeURIComponent(id)}`, { method: 'DELETE' }),
    webAuthnRegisterOptions: (label?: string) =>
      request<{
        rp: { id: string; name: string };
        user: { id: string; name: string; displayName: string };
        challenge: string;
        pubKeyCredParams: Array<{ type: 'public-key'; alg: number }>;
        authenticatorSelection?: { userVerification?: string; residentKey?: string };
        attestation?: string;
        timeout?: number;
        excludeCredentials?: Array<{ type: 'public-key'; id: string }>;
      }>('/api/auth/webauthn/register-options', { method: 'POST', body: JSON.stringify({ label: label ?? null }) }),
    webAuthnRegister: (body: { attestationObject: string; clientDataJson: string; label?: string }) =>
      request<{ id: string; credentialIdHash: string; label: string; attestationFormat: string }>(
        '/api/auth/webauthn/register',
        { method: 'POST', body: JSON.stringify(body) },
      ),
    webAuthnSignInOptions: (identity?: { tenant: string; user: string }) =>
      request<{
        challenge: string;
        rpId?: string;
        timeout?: number;
        userVerification?: UserVerificationRequirement;
        allowCredentials?: Array<{ type: 'public-key'; id: string }>;
      }>('/api/auth/webauthn/signin-options', {
        method: 'POST',
        body: JSON.stringify(identity ? { tenant: identity.tenant, user: identity.user } : {}),
      }),
    webAuthnSignIn: (body: {
      credentialId: string;
      clientDataJson: string;
      authenticatorData: string;
      signature: string;
      signCount: number;
      tenant?: string;
      user?: string;
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
  /**
   * Desktop↔phone companion pairing. The desktop (report open) advertises a
   * session and shows the code; the phone pairs by code and then streams
   * dictation over the WebSocket relay (`lib/companion.ts`). No PHI is sent to
   * these REST endpoints — only device names and the short-lived pairing code.
   */
  companion: {
    createSession: (deviceName: string) =>
      requestCompanion<CompanionSessionInit>('/api/companion/sessions', {
        method: 'POST',
        body: JSON.stringify({ deviceName }),
      }),
    pair: (pairingCode: string, deviceName: string) =>
      requestCompanion<CompanionPairResult>('/api/companion/pair', {
        method: 'POST',
        body: JSON.stringify({ pairingCode, deviceName }),
      }),
    endSession: (sessionId: string) =>
      requestCompanion<void>(`/api/companion/sessions/${sessionId}`, { method: 'DELETE' }),
    getSession: (sessionId: string) =>
      requestCompanion<CompanionSessionInfo>(`/api/companion/sessions/${sessionId}`),
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
