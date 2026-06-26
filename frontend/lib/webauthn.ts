/**
 * AUTH-001 — Windows Hello (fingerprint / face) sign-in via WebAuthn platform
 * authenticators. On Windows the platform authenticator IS Windows Hello, which
 * transparently uses the fingerprint reader or the front IR camera for face —
 * so this one flow covers both "fingerprint login" and "facial verification"
 * with no custom camera code. macOS Touch ID and Android biometrics work the
 * same way.
 *
 * The browser/OS performs the biometric check locally; only a signed assertion
 * crosses the wire. The backend (WebAuthnController) verifies the assertion
 * signature, the single-use challenge, and the origin/RP binding before minting
 * a session token.
 *
 * All helpers degrade gracefully: `isPlatformAuthenticatorAvailable()` lets the
 * UI hide the button when the device has no usable authenticator.
 */

import { api } from './api';

function bufToBase64Url(buf: ArrayBuffer): string {
  const bytes = new Uint8Array(buf);
  let bin = '';
  for (const b of bytes) bin += String.fromCharCode(b);
  return btoa(bin).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

function base64UrlToBuf(value: string): ArrayBuffer {
  const pad = value.replace(/-/g, '+').replace(/_/g, '/');
  const padded = pad + '==='.slice((pad.length + 3) % 4);
  const bin = atob(padded);
  const out = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
  return out.buffer;
}

/** Big-endian uint32 signature counter at offset 33 of authenticatorData. */
function readSignCount(authenticatorData: ArrayBuffer): number {
  const v = new DataView(authenticatorData);
  return v.byteLength >= 37 ? v.getUint32(33, false) : 0;
}

export function isWebAuthnSupported(): boolean {
  return typeof window !== 'undefined'
    && typeof window.PublicKeyCredential !== 'undefined'
    && typeof navigator !== 'undefined'
    && !!navigator.credentials;
}

/** True when the device exposes a built-in (platform) authenticator — Windows Hello, Touch ID, etc. */
export async function isPlatformAuthenticatorAvailable(): Promise<boolean> {
  if (!isWebAuthnSupported()) return false;
  try {
    return await window.PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable();
  } catch {
    return false;
  }
}

/**
 * Enrolls this device's Windows Hello (fingerprint/face) as a passkey for the
 * signed-in user. Must be called from an authenticated session — the backend
 * scopes the credential to the current tenant + user.
 */
export async function registerPasskey(label?: string): Promise<void> {
  if (!isWebAuthnSupported()) throw new Error('This device does not support passkeys / Windows Hello.');
  const opts = await api.auth.webAuthnRegisterOptions(label);

  const publicKey: PublicKeyCredentialCreationOptions = {
    rp: opts.rp,
    user: {
      id: base64UrlToBuf(opts.user.id),
      name: opts.user.name,
      displayName: opts.user.displayName,
    },
    challenge: base64UrlToBuf(opts.challenge),
    pubKeyCredParams: opts.pubKeyCredParams as PublicKeyCredentialParameters[],
    timeout: opts.timeout ?? 60_000,
    attestation: (opts.attestation as AttestationConveyancePreference) ?? 'none',
    // Force the on-device authenticator (Windows Hello) and require the user
    // verification gesture (PIN / fingerprint / face).
    authenticatorSelection: {
      authenticatorAttachment: 'platform',
      userVerification: 'required',
      residentKey: 'preferred',
    },
    excludeCredentials: (opts.excludeCredentials ?? []).map((c) => ({
      type: 'public-key' as const,
      id: base64UrlToBuf(c.id),
    })),
  };

  const cred = (await navigator.credentials.create({ publicKey })) as PublicKeyCredential | null;
  if (!cred) throw new Error('Passkey enrollment was cancelled.');
  const att = cred.response as AuthenticatorAttestationResponse;
  await api.auth.webAuthnRegister({
    attestationObject: bufToBase64Url(att.attestationObject),
    clientDataJson: bufToBase64Url(att.clientDataJSON),
    label,
  });
}

export type PasskeySignInResult = { token: string; tenant: string; user: string; expiresAt: string };

/**
 * Signs in with Windows Hello (fingerprint/face). On the login screen there is
 * no session yet, so `identity` (tenant slug + email) tells the server whose
 * credentials to offer; the assertion signature is what actually authenticates.
 * Returns the minted session token + tenant/user for the caller to persist via
 * `completeSession`.
 */
export async function signInWithPasskey(identity?: { tenant: string; user: string }): Promise<PasskeySignInResult> {
  if (!isWebAuthnSupported()) throw new Error('This device does not support passkeys / Windows Hello.');
  const opts = await api.auth.webAuthnSignInOptions(identity);

  const publicKey: PublicKeyCredentialRequestOptions = {
    challenge: base64UrlToBuf(opts.challenge),
    rpId: opts.rpId,
    timeout: opts.timeout ?? 60_000,
    userVerification: (opts.userVerification as UserVerificationRequirement) ?? 'preferred',
    allowCredentials: (opts.allowCredentials ?? []).map((c) => ({
      type: 'public-key' as const,
      id: base64UrlToBuf(c.id),
    })),
  };

  const assertion = (await navigator.credentials.get({ publicKey })) as PublicKeyCredential | null;
  if (!assertion) throw new Error('Sign-in was cancelled.');
  const resp = assertion.response as AuthenticatorAssertionResponse;
  return api.auth.webAuthnSignIn({
    credentialId: assertion.id,
    clientDataJson: bufToBase64Url(resp.clientDataJSON),
    authenticatorData: bufToBase64Url(resp.authenticatorData),
    signature: bufToBase64Url(resp.signature),
    signCount: readSignCount(resp.authenticatorData),
    tenant: identity?.tenant,
    user: identity?.user,
  });
}
