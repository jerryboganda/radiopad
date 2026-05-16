// PRD §21 — k6 load helper. Signs in via /api/auth/signin and returns a
// bearer token + tenant slug for subsequent requests. Reads:
//
//   __ENV.RADIOPAD_BASE_URL  (default http://127.0.0.1:7457)
//   __ENV.RADIOPAD_TENANT    (default 'it')
//   __ENV.RADIOPAD_USER      (default 'it-radiologist@radiopad.local')
//
// Never log or export the bearer — k6 prints scenario summaries to stdout.

import http from 'k6/http';
import { check } from 'k6';

export const BASE_URL = __ENV.RADIOPAD_BASE_URL || 'http://127.0.0.1:7457';
export const TENANT = __ENV.RADIOPAD_TENANT || 'it';
export const USER = __ENV.RADIOPAD_USER || 'it-radiologist@radiopad.local';

export function signIn() {
  const res = http.post(
    `${BASE_URL}/api/auth/signin`,
    JSON.stringify({ tenant: TENANT, user: USER }),
    { headers: { 'Content-Type': 'application/json' } },
  );
  check(res, { 'signin 200': (r) => r.status === 200 });
  const body = res.json();
  return body && body.token ? body.token : '';
}

export function authHeaders(token) {
  return {
    'Content-Type': 'application/json',
    'X-RadioPad-Tenant': TENANT,
    'X-RadioPad-User': USER,
    Authorization: token ? `Bearer ${token}` : '',
  };
}
