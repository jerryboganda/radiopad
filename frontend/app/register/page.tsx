'use client';

import { useMemo, useState } from 'react';
import Link from 'next/link';
import AuthScaffold from '@/components/auth/AuthScaffold';
import CheckYourEmail from '@/components/auth/CheckYourEmail';
import { api } from '@/lib/api';

/** Client-side slug derivation mirroring the backend's Slugify so the preview
 *  matches what the server will accept. */
function slugify(name: string): string {
  let slug = name
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
  if (slug.length > 40) slug = slug.slice(0, 40).replace(/-+$/g, '');
  return slug;
}

const SLUG_OK = /^[a-z0-9][a-z0-9-]{1,38}[a-z0-9]$/;

export default function RegisterPage() {
  const [orgName, setOrgName] = useState('');
  const [slug, setSlug] = useState('');
  const [slugTouched, setSlugTouched] = useState(false);
  const [email, setEmail] = useState('');
  const [name, setName] = useState('');
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const [sent, setSent] = useState<{ email: string; devLink?: string } | null>(null);

  const effectiveSlug = useMemo(
    () => (slugTouched ? slug.trim().toLowerCase() : slugify(orgName)),
    [slug, slugTouched, orgName],
  );
  const slugValid = effectiveSlug.length === 0 || SLUG_OK.test(effectiveSlug);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setErr(null);
    try {
      const callbackUrl = typeof window !== 'undefined' ? `${window.location.origin}/login` : undefined;
      const result = await api.registration.createOrganization({
        organizationName: orgName.trim(),
        slug: effectiveSlug || undefined,
        adminEmail: email.trim(),
        adminName: name.trim() || undefined,
        callbackUrl,
      });
      setSent({ email: email.trim(), devLink: result.devLink });
    } catch (e) {
      const ex = e as { body?: { error?: string; kind?: string }; message?: string };
      setErr(ex.body?.error || ex.message || 'We could not create your organization. Please try again.');
    } finally {
      setBusy(false);
    }
  }

  if (sent) {
    return (
      <AuthScaffold variant="register">
        <CheckYourEmail
          email={sent.email}
          devLink={sent.devLink}
          onBack={() => setSent(null)}
          backLabel="Back to sign-up"
        />
      </AuthScaffold>
    );
  }

  return (
    <AuthScaffold variant="register">
      <div className="rp-auth-head">
        <div className="rp-auth-eyebrow">Get started</div>
        <h1 className="rp-auth-title">Create your organization</h1>
        <p className="rp-auth-sub">
          Set up a new RadioPad workspace. We&rsquo;ll email you a secure link to finish — no password required.
        </p>
      </div>

      {err && <div className="banner danger" role="alert">{err}</div>}

      <form className="rp-auth-form" onSubmit={submit}>
        <div className="section-block">
          <label htmlFor="reg-org">Organization name</label>
          <input
            id="reg-org"
            value={orgName}
            onChange={(e) => setOrgName(e.target.value)}
            required
            autoComplete="organization"
            placeholder="Acme Radiology"
          />
        </div>

        <div className="section-block">
          <label htmlFor="reg-slug">Organization address</label>
          <input
            id="reg-slug"
            value={effectiveSlug}
            onChange={(e) => { setSlugTouched(true); setSlug(e.target.value); }}
            autoComplete="off"
            spellCheck={false}
            placeholder="acme-radiology"
            aria-invalid={!slugValid}
          />
          <p className={`rp-field-hint${slugValid ? '' : ' rp-field-error'}`}>
            {slugValid
              ? <>Your team will sign in under <code>{effectiveSlug || 'your-org'}</code>. Lowercase letters, numbers, and hyphens.</>
              : <>3–40 characters: lowercase letters, numbers, and hyphens only.</>}
          </p>
        </div>

        <div className="section-block">
          <label htmlFor="reg-email">Your work email</label>
          <input
            id="reg-email"
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
            autoComplete="email"
            placeholder="you@acme-radiology.org"
          />
        </div>

        <div className="section-block">
          <label htmlFor="reg-name">Your full name</label>
          <input
            id="reg-name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            autoComplete="name"
            placeholder="Dr. Jane Doe"
          />
        </div>

        <div className="rp-auth-actions">
          <button className="primary" type="submit" disabled={busy || !slugValid}>
            {busy ? 'Creating…' : 'Create organization'}
          </button>
        </div>
        <p className="rp-auth-hint">
          You&rsquo;ll be the first administrator. As the medical director you can draft and sign
          reports and invite your team afterwards.
        </p>
      </form>

      <div className="rp-auth-foot">
        Already have access? <Link className="rp-auth-link" href="/login">Sign in</Link>
      </div>
    </AuthScaffold>
  );
}
