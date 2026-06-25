'use client';

/**
 * Shared "check your email" success panel, used after a magic-link request and
 * after self-serve organization creation. `devLink` is only present in dev /
 * non-production responses and renders a copyable link so local users and
 * tests can complete the passwordless loop without a real mailbox.
 */

export default function CheckYourEmail({
  email,
  devLink,
  onBack,
  backLabel = 'Use a different email',
}: {
  email: string;
  devLink?: string | null;
  onBack?: () => void;
  backLabel?: string;
}) {
  return (
    <div className="rp-auth-success">
      <span className="rp-auth-success-icon" aria-hidden>
        <svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">
          <rect x="3" y="5" width="18" height="14" rx="2" />
          <path d="m3 7 9 6 9-6" />
        </svg>
      </span>
      <h2 className="rp-auth-success-title">Check your email</h2>
      <p className="rp-auth-success-sub">
        We sent a secure sign-in link to <strong>{email}</strong>. It expires soon and can be used once.
      </p>

      {devLink && (
        <div className="rp-auth-devlink">
          <div className="rp-auth-devlink-label">Dev link (non-production)</div>
          <code>
            <a href={devLink}>{devLink}</a>
          </code>
        </div>
      )}

      {onBack && (
        <div className="rp-auth-foot">
          <button type="button" className="rp-auth-link" onClick={onBack}>
            {backLabel}
          </button>
        </div>
      )}
    </div>
  );
}
