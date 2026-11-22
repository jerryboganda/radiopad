// Throwaway mock of the RadioPad API, only to look at screens without a backend (no dotnet on the dev box).
// Run: node docs/dev-tools/mock-api.js  -> 127.0.0.1:7457. Logs every request to %TEMP%/radiopad-mock-requests.log,
// marking EMPTY[] / EMPTY{} for endpoints it does not model yet. Shapes come from frontend/lib/api.ts.
// Never shipped: not imported by anything, not under frontend/.
const http = require('http');
const perms = ['reports.read', 'reports.draft', 'reports.edit', 'reports.validate', 'reports.sign', 'reports.export', 'rulebooks.read', 'templates.read', 'providers.read', 'audit.read', 'audit.verify', 'users.read', 'users.manage', 'billing.read', 'security.manage', 'tenant_settings.manage', 'validation_packs.read', 'mcp_tools.manage', 'prompt_overrides.manage', 'modalities.read', 'body_parts.read', 'critical_results.read', 'teaching_cases.read', 'peer_review.read'];
const now = Date.now();
const rep = (id, mod, part, status, mins, ind) => ({
  id, status, updatedAt: new Date(now - mins * 60000).toISOString(), createdAt: new Date(now - mins * 60000).toISOString(),
  indication: ind, technique: '', comparison: '', findings: id === 'r1' ? '8 mm solid nodule in the right upper lobe, unchanged.' : '', impression: '', recommendations: '',
  aiHighlightsJson: '{}', templateId: null, rulebookId: null, templatePinned: false, rulebookPinned: false,
  study: { accessionNumber: 'ACC-' + id.toUpperCase(), modality: mod, bodyPart: part, contrast: 'With', age: 58, ageUnit: 'Years', gender: 'Male', comparison: '' },
});
const reports = [rep('r1', 'CT', 'Chest', 'Draft', 3, 'Persistent cough. STAT.'), rep('r2', 'MR', 'Brain', 'Draft', 18, 'Headache'), rep('r3', 'XR', 'Chest', 'Validated', 50, 'Routine'), rep('r4', 'US', 'Abdomen', 'Exported', 300, 'Pain')];
const cat = (codes) => codes.map((c, i) => ({ id: 'c' + i, code: c, name: c, active: true, sortOrder: i }));
function body(url) {
  const p = url.split('?')[0].replace(/\/$/, '');
  if (p === '/api/tenant/me') return { tenant: { slug: 'dev', displayName: 'Dev Hospital' }, user: { email: 'radiologist@radiopad.local', role: 2, roleName: 'MedicalDirector', permissions: perms } };
  if (/^\/api\/reports\/(paged|search|query|list)$/.test(p)) return { items: reports, total: reports.length };
  if (p === '/api/reports/stats') return { total: 4, draft: 2, validated: 1, acknowledged: 0, exported: 1, trend: { total: [1,2,3,4], validated: [0,1,1,1], acknowledged: [0,0,0,0], exported: [0,0,1,1] } };
  if (p === '/api/reports') return reports;
  const m = p.match(/^\/api\/reports\/(r\d)$/);
  if (m) return reports.find((r) => r.id === m[1]);
  if (p === '/api/modalities') return cat(['CT', 'MR', 'XR', 'US']);
  if (p === '/api/body-parts') return cat(['Chest', 'Brain', 'Abdomen']);
  if (p === '/api/providers') return [{ id: 'p1', name: 'On-device formatter', adapter: 'llama-cpp', model: 'x', compliance: 4, enabled: true, priority: 1 }];
  const iso = new Date(now).toISOString(); const later = new Date(now + 20 * 864e5).toISOString();
  const tok = { calls: 120, inputTokens: 40000, outputTokens: 12000 };
  const usage = { window: { from: iso, to: iso }, totalRequests: 120, okCount: 110, blockedCount: 4, errorCount: 6, inputTokens: 40000, outputTokens: 12000, avgLatencyMs: 820, costTotalUsd: 1.92, byProvider: [{ provider: 'On-device formatter', adapter: 'llama-cpp', requests: 120, inputTokens: 40000, outputTokens: 12000, costInputUsd: 0, costOutputUsd: 0, costTotalUsd: 0, unpriced: false }] };
  if (p === '/api/users/roles') return { roles: ['Radiologist', 'SeniorRadiologist', 'MedicalDirector', 'TenantAdmin'].map((v, i) => ({ value: v, permissions: i + 1 })) };
  if (p === '/api/billing/status') return { plan: 'Trial', subscriptionStatus: null, trialEndsAt: later, gracePeriodUntil: null, suspendedAt: null, currentPeriodEnd: later, customerConfigured: false };
  if (p === '/api/billing/credits') return { plan: 'Trial', periodStart: iso, periodEnd: later, used: tok, limits: { calls: 1000, inputTokens: 1e6, outputTokens: 3e5 }, remaining: { calls: 880, inputTokens: 960000, outputTokens: 288000 }, trialEndsAt: later };
  if (p === '/api/usage/summary') return usage;
  if (p === '/api/notifications/unread-count') return { unread: 2, unacked: 1 };
  if (p === '/api/pacs/health') return { dicomWeb: { configured: false, reachable: false }, orthanc: { configured: false, reachable: false, url: null } };
  if (p === '/api/tenant/settings') return { id: 't1', hallucinationDetectionEnabled: true, hallucinationSeverity: 'Warning', hallucinationAllowList: '', hallucinationMinSupport: 0.5, plan: 0, featureFlagsJson: '{}', ipAllowlistJson: '[]', ingest: { bearerConfigured: false }, dicomWeb: { baseUrl: '', bearerConfigured: false }, pacs: { vendor: null }, stripe: { customerId: null, subscriptionId: null, status: null, currentPeriodEnd: null }, retention: { days: 2555, hashOnlyAuditMode: false, legalHold: false }, scim: { bearerConfigured: false }, cmk: { keyRef: null, lastVerifiedAt: null, configured: false }, validation: { requireZeroBlockers: true, warnAsBlocker: false }, reportLayouts: { recommendedId: null } };
  if (p === '/api/analytics/summary') return { window: { from: iso, to: iso }, product: { draftAcceptanceRate: 0.8, impressionAcceptanceRate: 0.7, timeSavedPerReport: 95, validationPassRate: 0.9, contradictionDetectionRate: 0.05, editDistance: 0.12, activeRadiologists: 3, rulebookAdoption: 0.6, providerCostPerReport: 0.02, turnaroundTimeImpact: -0.2, avgQualityScore: 4.1, reportsPerHour: 6 }, governance: { unapprovedPromptUsage: 0, phiViolationsBlocked: 2, rulebookRegressionFailures: 0, modelDriftAlerts: 1, auditCompleteness: 1 }, ai: usage };
  const rb = { id: 'rb1', rulebookId: 'chest-ct', name: 'Chest CT', version: '1.0.0', owner: 'Dr Lee', status: 'Approved', appliesToModalities: 'CT', appliesToBodyParts: 'Chest', updatedAt: iso, testsCount: 3, templatesCount: 1 };
  if (p === '/api/notifications') return { notifications: [], nextCursor: null };
  if (p === '/api/analytics/quality-trends') return { trends: [{ period: iso.slice(0, 10), avgScore: 86, reportCount: 4, blockerCount: 1 }], byRadiologist: [], byRulebook: [] };
  if (p === '/api/report-layouts') return { items: [], recommendedId: null, myDefaultId: null };
  if (p === '/api/local-models') return { enabled: true, models: [] };
  if (p === '/api/teaching-cases') return { total: 0, items: [] };
  if (p === '/api/siem/status') return { sinks: [] };
  if (p === '/api/rulebooks') return [rb];
  if (p === '/api/rulebooks/rb1') return { ...rb, sourceYaml: 'id: chest-ct\nname: Chest CT\nversion: 1.0.0\nrules: []\n' };
  if (p === '/api/rulebooks/validate') return { ok: true, problems: [] };
  if (p === '/api/validation-packs') return [];
  if (p === '/api/billing/features') return { plan: 'Trial', features: { aiDrafting: true, sso: false, peerReview: true } };
  if (p === '/api/auth/sessions') return { sessions: [] };
  if (p === '/api/auth/mfa/status') return { mfaEnabled: true };
  if (p === '/api/admin/observability/availability') return { windowSec: 3600, totalProbes: 60, errorCount: 0, errorRate: 0, lastCheckedAt: iso, targets: ['api'] };
  let mm;
  if ((mm = p.match(/^\/api\/reports\/(r\d)\/prior$/))) return { current: { id: mm[1], bodyPart: 'Chest' }, prior: null };
  if ((mm = p.match(/^\/api\/reports\/(r\d)\/compare-prior$/))) return { current: { id: mm[1], bodyPart: 'Chest' }, prior: null, sections: [] };
  if (/\/dictation\/cleanup\/jobs$/.test(p)) return { jobId: 'j1' };
  if (/\/ai\/jobs\/[^/]+$/.test(p)) return { jobId: 'j1', kind: 'cleanup', mode: 'async', status: 'failed', elapsedMs: 10, result: null, error: 'Mock: no AI engine', errorKind: 'unavailable' };
  if (/\/validate$/.test(p)) return { blockerPresent: false, findings: [], qualityScore: 92 };
  if (/validate|validation/.test(p)) return { blockerPresent: false, findings: [] };
  if (/\/(stats|summary|status|settings|me|count|health|ready)$/.test(p)) return {};
  return [];
}
// Writes that change state, so the reporting flow can be walked end to end.
const signatures = {};
const take = (id, rid, secs, status, text) => ({ id, reportId: rid, storagePath: '', durationSeconds: secs, transcriptionEngine: 'medasr-6gram', status, transcribedText: text, uploadedAt: new Date(now - 30 * 60000).toISOString(), transcribedAt: text ? new Date(now - 25 * 60000).toISOString() : null, errorMessage: null });
const mobileReports = [
  { id: 'm1', radiologyId: 'RAD-2026-4411', patientName: 'Jane Doe', patientAge: 64, patientGender: 'Female', createdAt: new Date(now - 40 * 60000).toISOString(), status: 'Pending', modality: 'CT', bodyPart: 'Abdomen', dictations: [take('d1', 'm1', 42, 'Transcribed', 'Liver normal in size. No focal lesion.'), take('d2', 'm1', 18, 'Uploaded', null)] },
  { id: 'm2', radiologyId: 'RAD-2026-4412', patientName: 'Mohammed Al-Rashidi Abdulrahman', patientAge: 7, patientGender: 'Male', createdAt: new Date(now - 300 * 60000).toISOString(), status: 'Completed', modality: 'XR', bodyPart: 'Chest', dictations: [] },
];
function mutate(method, p, j) {
  const iso = new Date().toISOString();
  const r = (id) => reports.find((x) => x.id === id);
  let m;
  if (method === 'GET' && p === '/api/ubag/status') return { health: { ok: false, status: 'unreachable', error: 'Mock: no gateway' }, browser: { instances: 0, contexts: 0, tabs: 0 }, targets: [], allowedTargets: [], orderedTargets: [], alerts: [] };
  if (method === 'GET' && (m = p.match(/^\/api\/reports\/(r\d)\/signatures$/))) return signatures[m[1]] || [];
  // Mobile companion "Reporting" tab (lib/api/reportingClient.ts).
  if (method === 'GET' && p === '/api/v1/reporting/reports/transcription-engines') return [{ engineId: 'medasr-6gram', displayName: 'medASR 6-gram (on-device)', isLocal: true, isAvailable: true, isDefault: true }];
  if (method === 'GET' && p === '/api/v1/reporting/reports') return mobileReports;
  if (method === 'GET' && (m = p.match(/^\/api\/v1\/reporting\/reports\/([^/]+)$/))) return mobileReports.find((x) => x.id === m[1]);
  if ((m = p.match(/^\/api\/rulebooks\/([^/]+)\/(approve|deprecate)$/))) return { id: m[1], rulebookId: 'chest-ct', name: 'Chest CT', version: '1.0.0', owner: 'Dr Lee', status: m[2] === 'approve' ? 'Approved' : 'Deprecated', appliesToModalities: 'CT', appliesToBodyParts: 'Chest' };
  if ((method === 'PUT' || method === 'PATCH') && (m = p.match(/^\/api\/reports\/(r\d)$/)) && r(m[1])) return Object.assign(r(m[1]), j, { updatedAt: iso });
  if (method !== 'POST') return undefined;
  if ((m = p.match(/^\/api\/reports\/(r\d)\/validate$/)) && r(m[1])) { if (r(m[1]).status === 'Draft') r(m[1]).status = 'Validated'; return { blockerPresent: false, findings: [], qualityScore: 92 }; }
  if ((m = p.match(/^\/api\/reports\/(r\d)\/acknowledge$/)) && r(m[1])) return Object.assign(r(m[1]), { status: 'Acknowledged', updatedAt: iso });
  if ((m = p.match(/^\/api\/reports\/(r\d)\/sign$/)) && r(m[1])) { const s = { id: 's' + Date.now(), reportId: m[1], radiologistEmail: 'radiologist@radiopad.local', role: 'Primary', note: j.note ?? null, signedAt: iso }; (signatures[m[1]] ||= []).push(s); Object.assign(r(m[1]), { status: 'Exported', updatedAt: iso }); return s; }
  return undefined;
}
http.createServer((req, res) => {
  res.setHeader('Access-Control-Allow-Origin', req.headers.origin || '*');
  res.setHeader('Access-Control-Allow-Credentials', 'true');
  res.setHeader('Access-Control-Allow-Headers', req.headers['access-control-request-headers'] || '*');
  res.setHeader('Access-Control-Allow-Methods', 'GET,POST,PUT,PATCH,DELETE,OPTIONS');
  if (req.method === 'OPTIONS') return res.writeHead(204).end();
  res.setHeader('Content-Type', 'application/json');
  let raw = '';
  req.on('data', (c) => (raw += c));
  req.on('end', () => {
  let json = {}; try { json = raw ? JSON.parse(raw) : {}; } catch {}
  const b = mutate(req.method, req.url.split('?')[0].replace(/\/$/, ''), json) ?? body(req.url);
  require('fs').appendFileSync(require('os').tmpdir() + '/radiopad-mock-requests.log', req.method + ' ' + req.url.split('?')[0] + ' ' + (Array.isArray(b) && !b.length ? 'EMPTY[]' : JSON.stringify(b) === '{}' ? 'EMPTY{}' : 'ok') + '\n');
  res.end(JSON.stringify(b));
  });
}).listen(7457, '127.0.0.1', () => console.log('mock api on 7457'));
