// PRD §21 SLO — Impression generation P95 < 5s.
// Exercises POST /api/reports/{id}/ai with kind=Impression and asserts the
// faster impression-only SLO (lower than full draft).

import http from 'k6/http';
import { check, sleep } from 'k6';
import { BASE_URL, authHeaders, signIn } from '../lib/auth.js';

export const options = {
  scenarios: {
    impression: {
      executor: 'constant-vus',
      vus: Number(__ENV.K6_VUS || 10),
      duration: __ENV.K6_DURATION || '60s',
    },
  },
  thresholds: {
    'http_req_failed{endpoint:impression}': ['rate<0.05'],
    'http_req_duration{endpoint:impression}': ['p(95)<5000'],
  },
};

export function setup() {
  const t = signIn();
  const res = http.post(
    `${BASE_URL}/api/reports`,
    JSON.stringify({
      modality: 'CT',
      bodyPart: 'Chest',
      indication: 'k6 perf — impression',
      accessionNumber: `K6IMP-${Date.now()}`,
    }),
    { headers: authHeaders(t) },
  );
  check(res, { 'seed 200/201': (r) => r.status === 200 || r.status === 201 });
  return { token: t, reportId: res.json('id') };
}

export default function (data) {
  const res = http.post(
    `${BASE_URL}/api/reports/${data.reportId}/ai`,
    JSON.stringify({ kind: 'Impression', mode: 'impression' }),
    { headers: authHeaders(data.token), tags: { endpoint: 'impression' } },
  );
  check(res, { 'impression 2xx': (r) => r.status >= 200 && r.status < 300 });
  sleep(0.2);
}
