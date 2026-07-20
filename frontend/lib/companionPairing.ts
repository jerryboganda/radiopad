/**
 * Shared codec for the desktop→phone pairing QR.
 *
 * The desktop host encodes everything the phone needs to authenticate AND pair
 * in a single QR: the cloud relay base, the short pairing code, and the
 * short-lived companion bearer (plus its tenant/user context). The phone scans
 * it, adopts the token as its auth bearer, and calls `pair` — so there is no
 * separate phone login (that was the whole "Pairing failed" bug: the phone had
 * no way to authenticate).
 *
 * The payload is a compact JSON string tagged with `k:'rp-companion'` + `v:1`
 * so the mobile parser can reject anything that isn't one of our QRs (a generic
 * QR reader just shows it as opaque text — no URL is auto-opened). The bearer is
 * a credential: it only ever lives on the radiologist's own desktop screen for
 * the seconds it takes them to scan it, and it expires + is revocable server-side.
 */

export const COMPANION_PAIRING_KIND = 'rp-companion';
export const COMPANION_PAIRING_VERSION = 1;

export interface CompanionPairingPayload {
  /** Cloud relay origin the phone addresses (`companionBase()`), e.g. https://admin.radiopadstudio.com */
  base: string;
  /** Short pairing code (also shown for manual fallback). */
  code: string;
  /** Short-lived companion bearer the phone adopts as its auth token. */
  token: string;
  /** Bearer tenant slug (the `X-RadioPad-Tenant` context). */
  tenant: string;
  /** Bearer user email (the `X-RadioPad-User` context). */
  user: string;
}

interface Wire {
  k: string;
  v: number;
  b: string;
  c: string;
  t: string;
  tn: string;
  u: string;
}

/** Encode a payload into the string carried by the QR. */
export function encodeCompanionPairing(p: CompanionPairingPayload): string {
  const wire: Wire = {
    k: COMPANION_PAIRING_KIND,
    v: COMPANION_PAIRING_VERSION,
    b: p.base,
    c: p.code,
    t: p.token,
    tn: p.tenant,
    u: p.user,
  };
  return JSON.stringify(wire);
}

/**
 * Parse a scanned/pasted string back into a payload. Returns `null` for anything
 * that is not one of our current-version pairing QRs or is missing a field —
 * the caller renders a friendly "that's not a RadioPad pairing code" message
 * rather than trying to pair with garbage.
 */
export function decodeCompanionPairing(raw: string): CompanionPairingPayload | null {
  const text = (raw ?? '').trim();
  if (!text || text[0] !== '{') return null;
  let wire: Partial<Wire>;
  try {
    wire = JSON.parse(text) as Partial<Wire>;
  } catch {
    return null;
  }
  if (wire.k !== COMPANION_PAIRING_KIND || wire.v !== COMPANION_PAIRING_VERSION) return null;
  const base = typeof wire.b === 'string' ? wire.b.trim() : '';
  const code = typeof wire.c === 'string' ? wire.c.trim() : '';
  const token = typeof wire.t === 'string' ? wire.t.trim() : '';
  const tenant = typeof wire.tn === 'string' ? wire.tn.trim() : '';
  const user = typeof wire.u === 'string' ? wire.u.trim() : '';
  if (!base || !code || !token || !tenant || !user) return null;
  return { base, code, token, tenant, user };
}
