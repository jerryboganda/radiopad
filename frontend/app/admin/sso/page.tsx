'use client';

import PermissionGate from '@/components/ui/PermissionGate';

import { useCallback, useEffect, useState } from 'react';
import { api, type WebAuthnCredentialRow } from '@/lib/api';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import Banner from '@/components/ui/Banner';
import EmptyState from '@/components/ui/EmptyState';

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
  return (
    <PermissionGate permission="security.manage" title="Single sign-on">
      <SsoAdminPageInner />
    </PermissionGate>
  );
}

function SsoAdminPageInner() {
  const [selected, setSelected] = useState<string>('keycloak');
  const [creds, setCreds] = useState<Credential[]>([]);
  const [error, setError] = useState<string | null>(null);
  const profile = PRESETS.find((p) => p.name === selected) ?? PRESETS[0];

  const loadCreds = useCallback(() => {
    setError(null);
    api.auth.webAuthnCredentials()
      .then(setCreds)
      .catch((e: Error) => setError(e.message));
  }, []);

  useEffect(() => {
    loadCreds();
  }, [loadCreds]);

  return (
    <Container>
      <PageHeader
        title="Single sign-on (SSO)"
        description="Let your team sign in to RadioPad using your hospital's existing identity system, so they don't need a separate password."
      />

      <div aria-live="polite">
        {error && (
          <Banner tone="warn" title="Couldn't load passkey enrolments">
            {error}{' '}
            <button type="button" className="subtle" onClick={loadCreds}>Try again</button>
          </Banner>
        )}
      </div>

      <div className="rp-page-grid">
        <div className="rp-page-main rp-stagger">

      <div className="rp-panel rp-anim-fade-in-up">
        <div className="rp-panel-title">Identity system setup</div>
        <p className="rp-page-sub">
          Pick the identity system your hospital uses, then share the notes below with your IT team. They&apos;ll finish the setup.
        </p>
        <label className="rp-field">
          <span>Identity system</span>
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
        <p className="rp-page-sub"><strong>For your IT team:</strong> {profile.operatorNotes}</p>
        <details className="rp-advanced">
          <summary>Show technical settings (IT team only)</summary>
          <dl className="rp-defs">
            <dt>Tenant claim</dt>
            <dd><code>{profile.defaultTenantClaim}</code></dd>
            <dt>Email claim</dt>
            <dd><code>{profile.defaultEmailClaim}</code></dd>
            <dt>Require MFA</dt>
            <dd>{profile.defaultRequireMfa ? 'Yes' : 'No'}</dd>
          </dl>
          <p className="rp-page-sub">
            To activate, set <code>RADIOPAD_OIDC_PRESET={profile.name}</code>{' '}
            plus your <code>RADIOPAD_OIDC_AUTHORITY</code> /{' '}
            <code>RADIOPAD_OIDC_AUDIENCE</code> on the API host.
          </p>
        </details>
      </div>

      <details className="rp-panel rp-advanced rp-anim-fade-in-up">
        <summary className="rp-panel-title" style={{ cursor: 'pointer' }}>SAML 2.0 (IT team only)</summary>
        <p className="rp-page-sub">
          The service-provider metadata document is at{' '}
          <a href="/saml/metadata" target="_blank" rel="noreferrer"><code>/saml/metadata</code></a>.
          Configure <code>RADIOPAD_SAML_IDP_CERT_PEM</code> and{' '}
          <code>RADIOPAD_SAML_TENANT_ATTRIBUTE</code> (default <code>tenant_slug</code>).
          The ACS endpoint is <code>POST /saml/acs</code>.
        </p>
        <a className="primary" href="/saml/metadata" target="_blank" rel="noreferrer">
          Download metadata
        </a>
      </details>

      <div className="rp-panel rp-anim-fade-in-up">
        <div className="rp-panel-title">Passkeys / fingerprint sign-in</div>
        <p className="rp-page-sub">
          Lets each radiologist sign in with a fingerprint or device PIN instead of a password.
          Users enrol from their own profile page. You can see who&apos;s enrolled below.
        </p>
        {creds.length === 0 ? (
          <EmptyState
            title="No passkeys enrolled yet"
            description="Radiologists enrol a fingerprint or device PIN from their own profile page. Once they do, they'll appear here."
          />
        ) : (
          <table className="rp-table">
            <thead>
              <tr>
                <th>Name</th>
                <th>Enrolled</th>
                <th>Last used</th>
              </tr>
            </thead>
            <tbody>
              {creds.map((c) => (
                <tr key={c.id}>
                  <td>{c.label || <em>(unnamed device)</em>}</td>
                  <td>{new Date(c.createdAt).toLocaleString()}</td>
                  <td>{c.lastUsedAt ? new Date(c.lastUsedAt).toLocaleString() : '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

        </div>
        <aside className="rp-page-aside">
          <div className="rp-help">
            <div className="rp-help-title">What is single sign-on?</div>
            <p>It means your team uses their normal hospital login (the one they use for email and other apps) to sign in to RadioPad. No extra password to remember.</p>
          </div>
          <div className="rp-help">
            <div className="rp-help-title">Who sets this up?</div>
            <p>Your IT team. Share this page with them — the technical notes are tucked under &ldquo;Show technical settings&rdquo;.</p>
          </div>
        </aside>
      </div>
    </Container>
  );
}
