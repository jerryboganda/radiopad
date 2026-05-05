'use client';

import { useEffect, useState } from 'react';
import { api, type WebAuthnCredentialRow } from '@/lib/api';

type Profile = {
  name: string;
  defaultTenantClaim: string;
  defaultEmailClaim: string;
  defaultRequireMfa: boolean;
  amrMfaValueHint: string | null;
  operatorNotes: string;
};

type Credential = WebAuthnCredentialRow;

const PRESETS: Profile[] = [
  {
    name: 'keycloak',
    defaultTenantClaim: 'tenant_slug',
    defaultEmailClaim: 'email',
    defaultRequireMfa: true,
    amrMfaValueHint: 'mfa',
    operatorNotes:
      "Add a 'User Attribute' protocol mapper named 'tenant_slug' on the realm client; check 'Add to ID token' and 'Add to access token'.",
  },
  {
    name: 'auth0',
    defaultTenantClaim: 'https://radiopad/tenant_slug',
    defaultEmailClaim: 'email',
    defaultRequireMfa: true,
    amrMfaValueHint: 'mfa',
    operatorNotes:
      "Use an Auth0 Action to add 'tenant_slug' as a custom claim under the 'https://radiopad/' namespace; enable MFA enrollment for the connection.",
  },
  {
    name: 'okta',
    defaultTenantClaim: 'tenant_slug',
    defaultEmailClaim: 'email',
    defaultRequireMfa: true,
    amrMfaValueHint: 'mfa',
    operatorNotes:
      "Add a custom claim 'tenant_slug' on the authorization server with expression 'user.tenant_slug'; require MFA via the Okta sign-on policy.",
  },
];

export default function SsoAdminPage() {
  const [selected, setSelected] = useState<string>('keycloak');
  const [creds, setCreds] = useState<Credential[]>([]);
  const [error, setError] = useState<string | null>(null);
  const profile = PRESETS.find((p) => p.name === selected) ?? PRESETS[0];

  useEffect(() => {
    api.auth.webAuthnCredentials()
      .then(setCreds)
      .catch((e: Error) => setError(e.message));
  }, []);

  return (
    <div className="rp-container">
      <h1 className="rp-page-title">Single sign-on</h1>
      <p className="rp-page-sub">
        Pick the IdP you have already provisioned and follow the operator
        notes. The backend reads <code>RADIOPAD_OIDC_*</code> env vars at
        request time; this page only emits guidance — secrets are never
        captured by the UI.
      </p>

      {error && <div className="banner warn">{error}</div>}

      <div className="rp-panel">
        <div className="rp-panel-title">OIDC preset</div>
        <label className="rp-field">
          <span>Provider</span>
          <select
            className="rp-input"
            value={selected}
            onChange={(e) => setSelected(e.target.value)}
          >
            {PRESETS.map((p) => (
              <option key={p.name} value={p.name}>
                {p.name}
              </option>
            ))}
          </select>
        </label>
        <dl className="rp-defs">
          <dt>Tenant claim</dt>
          <dd>
            <code>{profile.defaultTenantClaim}</code>
          </dd>
          <dt>Email claim</dt>
          <dd>
            <code>{profile.defaultEmailClaim}</code>
          </dd>
          <dt>Require MFA</dt>
          <dd>{profile.defaultRequireMfa ? 'Yes' : 'No'}</dd>
        </dl>
        <p className="rp-page-sub">{profile.operatorNotes}</p>
        <p className="rp-page-sub">
          To activate, set <code>RADIOPAD_OIDC_PRESET={profile.name}</code>{' '}
          plus your IdP-specific{' '}
          <code>RADIOPAD_OIDC_AUTHORITY</code> /{' '}
          <code>RADIOPAD_OIDC_AUDIENCE</code> in the API process environment.
        </p>
      </div>

      <div className="rp-panel">
        <div className="rp-panel-title">SAML 2.0</div>
        <p className="rp-page-sub">
          The service-provider metadata document is served at{' '}
          <a href="/saml/metadata" target="_blank" rel="noreferrer">
            <code>/saml/metadata</code>
          </a>
          . Configure the IdP signing certificate via{' '}
          <code>RADIOPAD_SAML_IDP_CERT_PEM</code> and the tenant attribute
          name via <code>RADIOPAD_SAML_TENANT_ATTRIBUTE</code> (default{' '}
          <code>tenant_slug</code>). The Assertion Consumer Service endpoint
          is <code>POST /saml/acs</code>.
        </p>
        <a className="primary" href="/saml/metadata" target="_blank" rel="noreferrer">
          Download metadata
        </a>
      </div>

      <div className="rp-panel">
        <div className="rp-panel-title">Passkeys / WebAuthn</div>
        <p className="rp-page-sub">
          Each user can enrol a FIDO2 / passkey credential from their
          profile page. Admins can audit enrolment from this list. Removing
          a credential here invalidates it immediately for the user.
        </p>
        {creds.length === 0 ? (
          <p className="rp-page-sub">No credentials enrolled.</p>
        ) : (
          <table className="rp-table">
            <thead>
              <tr>
                <th>Label</th>
                <th>Created</th>
                <th>Last used</th>
                <th>Sign count</th>
              </tr>
            </thead>
            <tbody>
              {creds.map((c) => (
                <tr key={c.id}>
                  <td>{c.label || <em>unlabelled</em>}</td>
                  <td>{new Date(c.createdAt).toLocaleString()}</td>
                  <td>{c.lastUsedAt ? new Date(c.lastUsedAt).toLocaleString() : '—'}</td>
                  <td>
                    <code>{c.signCount}</code>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
