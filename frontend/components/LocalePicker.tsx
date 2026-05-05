'use client';

/**
 * Iter-35 i18n — topbar locale picker. Inline `<select>` styled with the
 * existing `.subtle` class (see `frontend/app/globals.css`). Writing a
 * value:
 *   1. updates the `radiopad-locale` cookie via `writeLocaleCookie` so
 *      subsequent SSR / static requests resolve the same tag,
 *   2. fires `PUT /api/users/me/locale` so the per-user override sticks
 *      across browsers (best-effort; failures are silent — the cookie is
 *      authoritative for the current session).
 *   3. reloads the page so the next render picks up the new bundle.
 *
 * Clearing the picker (selecting the "auto" option) clears both the cookie
 * and the user override; the next negotiation falls back through
 * `Accept-Language` → tenant default → `en`.
 */

import { useEffect, useState } from 'react';
import {
  SUPPORTED_LOCALES,
  type Locale,
  LOCALE_COOKIE,
  pickClientLocale,
  writeLocaleCookie,
} from '@/lib/i18n';
import { api } from '@/lib/api';

const LABELS: Record<Locale, string> = {
  en: 'English',
  es: 'Español',
  de: 'Deutsch',
  fr: 'Français',
  pt: 'Português',
  hi: 'हिन्दी',
};

export default function LocalePicker({ ariaLabel }: { ariaLabel: string }) {
  const [active, setActive] = useState<Locale | null>(null);

  useEffect(() => {
    setActive(pickClientLocale());
  }, []);

  function onChange(e: React.ChangeEvent<HTMLSelectElement>) {
    const value = e.target.value;
    if (value === 'auto') {
      // Clear cookie + user override.
      document.cookie = `${LOCALE_COOKIE}=; Max-Age=0; Path=/; SameSite=Lax`;
      api.users.me.setLocale(null).catch(() => {});
    } else {
      const next = value as Locale;
      writeLocaleCookie(next);
      api.users.me.setLocale(next).catch(() => {});
    }
    // Hard reload so server + client message bundles realign.
    if (typeof window !== 'undefined') window.location.reload();
  }

  return (
    <select
      className="subtle"
      aria-label={ariaLabel}
      value={active ?? 'en'}
      onChange={onChange}
    >
      <option value="auto">Auto</option>
      {SUPPORTED_LOCALES.map((loc) => (
        <option key={loc} value={loc}>
          {LABELS[loc]}
        </option>
      ))}
    </select>
  );
}
