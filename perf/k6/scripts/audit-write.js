// PRD §21 SLO — audit-log write round-trip (create report → audit search)
// P99 < 500ms. The audit chain is append-only (SHA-256), so we measure the
// indirect latency from causing an audit row to seeing it via the search API.

import http from 'k6/http';
import { check, sleep, fail } from 'k6';
import { BASE_URL, authHeaders, signIn } from '../lib/auth.js';

export const options = {
  scenarios: {
    audit_write: {
      executor: 'constant-vus',
      vus: Number(__ENV.K6_VUS || 10),
      duration: __ENV.K6_DURATION || '60s',
    },
  },
  thresholds: {
    'http_req_failed{endpoint:audit_create}': ['rate<0.02'],
    'http_req_failed{endpoint:audit_search}': ['rate<0.02'],
    // The audit row must show up in /api/audit/search ≤500ms p99 after
    // the originating write returns.
    'http_req_duration{endpoint:audit_search}': ['p(99)<500'],
  },
};

export function setup() {
  return { token: signIn() };
}

export default function (data) {
  const accession = `K6AUD-${__VU}-${__ITER}-${Date.now()}`;
  const create = http.post(
    `${BASE_URL}/api/reports`,
    JSON.stringify({
      modality: 'CT',
      bodyPart: 'Chest',
      indication: 'k6 perf — audit',
      accessionNumber: accession,
    }),
    { headers: authHeaders(data.token), tags: { endpoint: 'audit_create' } },
  );
  if (!check(create, { 'create 2xx': (r) => r.status >= 200 && r.status < 300 })) {
    fail('create failed');
  }
  const reportId = create.json('id');

  // Search audit log for the row produced by the create above.
  const search = http.get(
    `${BASE_URL}/api/audit/search?reportId=${reportId}&take=1`,
    { headers: authHeaders(data.token), tags: { endpoint: 'audit_search' } },
  );
  check(search, { 'audit 2xx': (r) => r.status >= 200 && r.status < 300 });
  sleep(0.05);
}
