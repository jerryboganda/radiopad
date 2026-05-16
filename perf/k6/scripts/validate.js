// PRD §21 SLO — POST /api/reports/{id}/validate P95 < 3s.

import http from 'k6/http';
import { check, sleep } from 'k6';
import { BASE_URL, authHeaders, signIn } from '../lib/auth.js';

export const options = {
  scenarios: {
    validate: {
      executor: 'constant-vus',
      vus: Number(__ENV.K6_VUS || 10),
      duration: __ENV.K6_DURATION || '60s',
    },
  },
  thresholds: {
    'http_req_failed{endpoint:validate}': ['rate<0.02'],
    'http_req_duration{endpoint:validate}': ['p(95)<3000'],
  },
};

export function setup() {
  const t = signIn();
  const res = http.post(
    `${BASE_URL}/api/reports`,
    JSON.stringify({
      modality: 'CT',
      bodyPart: 'Chest',
      indication: 'k6 perf — validate',
      accessionNumber: `K6VAL-${Date.now()}`,
    }),
    { headers: authHeaders(t) },
  );
  check(res, { 'seed 200/201': (r) => r.status === 200 || r.status === 201 });
  return { token: t, reportId: res.json('id') };
}

export default function (data) {
  const res = http.post(
    `${BASE_URL}/api/reports/${data.reportId}/validate`,
    null,
    { headers: authHeaders(data.token), tags: { endpoint: 'validate' } },
  );
  check(res, { 'validate 2xx': (r) => r.status >= 200 && r.status < 300 });
  sleep(0.1);
}
