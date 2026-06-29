import type { ReactNode } from 'react';
import { Info, CheckCircle2, AlertTriangle, XCircle, Sparkles, X } from 'lucide-react';

export type BannerTone = 'info' | 'success' | 'warn' | 'danger' | 'ai';

const TONE_ICON = {
  info: Info,
  success: CheckCircle2,
  warn: AlertTriangle,
  danger: XCircle,
  ai: Sparkles,
} as const;

export interface BannerProps {
  tone?: BannerTone;
  title?: ReactNode;
  children?: ReactNode;
  /** Override the default tone icon. */
  icon?: ReactNode;
  /** Render a dismiss button wired to this handler. */
  onDismiss?: () => void;
  /** aria-live politeness. Defaults: danger→assertive, otherwise polite. */
  live?: 'polite' | 'assertive' | 'off';
  className?: string;
}

/**
 * Standardized status banner with a token-tinted tone, a lucide icon, an
 * entrance animation, and built-in a11y (role + aria-live). Replaces the
 * ad-hoc inline `.banner` markup scattered across pages (which appeared
 * instantly and caused layout shift). Semantic tones map to the locked
 * palette: info→marine, success→green, warn→amber, danger→red, ai→purple.
 */
export default function Banner({
  tone = 'info',
  title,
  children,
  icon,
  onDismiss,
  live,
  className,
}: BannerProps) {
  const Icon = TONE_ICON[tone];
  const role = tone === 'danger' || tone === 'warn' ? 'alert' : 'status';
  const ariaLive = live ?? (tone === 'danger' ? 'assertive' : 'polite');
  const cls = ['rp-banner', `rp-banner-${tone}`, 'rp-anim-fade-in-down', className]
    .filter(Boolean)
    .join(' ');

  return (
    <div className={cls} role={role} aria-live={ariaLive}>
      <span className="rp-banner-icon" aria-hidden>
        {icon ?? <Icon size={18} strokeWidth={1.8} />}
      </span>
      <div className="rp-banner-body">
        {title && <p className="rp-banner-title">{title}</p>}
        {children && <div className="rp-banner-text">{children}</div>}
      </div>
      {onDismiss && (
        <button type="button" className="rp-banner-dismiss" onClick={onDismiss} aria-label="Dismiss">
          <X size={16} strokeWidth={2} />
        </button>
      )}
    </div>
  );
}
