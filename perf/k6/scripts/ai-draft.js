// PRD §21 SLO — POST /api/reports/{id}/ai P95 < 10s.
// Drives the AI gateway with a Mock provider so the test exercises the
// validation + audit path without consuming external quota.

import http from 'k6/http';
import { check, sleep } from 'k6';
import { BASE_URL, authHeaders, signIn } from '../lib/auth.js';

export const options = {
  scenarios: {
    ai_draft: {
      executor: 'constant-vus',
      vus: Number(__ENV.K6_VUS || 10),
      duration: __ENV.K6_DURATION || '60s',
    },
  },
  thresholds: {
    'http_req_failed{endpoint:ai}': ['rate<0.05'],
    'http_req_duration{endpoint:ai}': ['p(95)<10000'],
  },
};

let token;
let reportId;

export function setup() {
  const t = signIn();
  // Seed a single draft report we will reuse for AI calls.
  const res = http.post(
    `${BASE_URL}/api/reports`,
    JSON.stringify({
      modality: 'CT',
      bodyPart: 'Chest',
      indication: 'k6 perf test — AI draft',
      accessionNumber: `K6-${Date.now()}`,
    }),
    { headers: authHeaders(t) },
  );
  check(res, { 'seed report 200/201': (r) => r.status === 200 || r.status === 201 });
  const id = res.json('id');
  return { token: t, reportId: id };
}

export default function (data) {
  const res = http.post(
    `${BASE_URL}/api/reports/${data.reportId}/ai`,
    JSON.stringify({ kind: 'Impression' }),
    { headers: authHeaders(data.token), tags: { endpoint: 'ai' } },
  );
  check(res, { 'ai 2xx': (r) => r.status >= 200 && r.status < 300 });
  sleep(0.2);
}
