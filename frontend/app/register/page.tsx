'use client';

import Link from 'next/link';
import AuthScaffold from '@/components/auth/AuthScaffold';

/**
 * Organization creation is super-admin / seed only — there is no public,
 * email-based self-serve sign-up (it would consume the server email quota and
 * is intentionally locked down). New organizations and their first master admin
 * are provisioned by an operator (`radiopad org create`), and users within an
 * org are managed by that org's administrator. This screen explains the flow and
 * routes people back to sign-in.
 */
export default function RegisterPage() {
  return (
    <AuthScaffold variant="register">
      <div className="rp-auth-head">
        <div className="rp-auth-eyebrow">Invite only</div>
        <h1 className="rp-auth-title">Accounts are created by your admin</h1>
        <p className="rp-auth-sub">
          RadioPad organizations and user accounts are provisioned by an administrator — there is
          no public sign-up. This keeps every tenant&rsquo;s data isolated and access fully audited.
        </p>
      </div>

      <ol className="rp-auth-steplist">
        <li>
          <span className="rp-auth-steplist-num">1</span>
          <span>
            <strong>Your administrator creates your account</strong> and hands you a temporary password.
          </span>
        </li>
        <li>
          <span className="rp-auth-steplist-num">2</span>
          <span>
            <strong>Sign in</strong> with your organization, email, and that password.
          </span>
        </li>
        <li>
          <span className="rp-auth-steplist-num">3</span>
          <span>
            <strong>Set up your authenticator app</strong> (and optionally fingerprint / face) — then you&rsquo;re in.
          </span>
        </li>
      </ol>

      <div className="rp-auth-actions">
        <Link className="primary rp-auth-cta" href="/login">Go to sign in</Link>
      </div>

      <div className="rp-auth-foot">
        Are you an operator setting up a new organization? Use the{' '}
        <code className="rp-inline-code">radiopad org create</code> CLI command.
      </div>
    </AuthScaffold>
  );
}
