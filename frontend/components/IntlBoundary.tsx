'use client';

/**
 * Iter-35 i18n — client-side `NextIntlClientProvider` wrapper.
 *
 * The frontend is a static export (`output: 'export'` in
 * `frontend/next.config.ts`), so locale negotiation happens client-side
 * via `pickClientLocale`. This boundary loads the matching message bundle
 * synchronously (all six bundles are already imported by `lib/i18n.ts`)
 * and feeds it to `NextIntlClientProvider` so descendant client
 * components can call `useTranslations`.
 *
 * Clinical content (rulebooks, finding text, validation messages emitted
 * by the rulebook engine) is never translated; the bundles only carry
 * chrome strings.
 */

import { useEffect, useState, type ReactNode } from 'react';
import { NextIntlClientProvider } from 'next-intl';
import {
  DEFAULT_LOCALE,
  type Locale,
  getMessages,
  pickClientLocale,
} from '@/lib/i18n';

export default function IntlBoundary({ children }: { children: ReactNode }) {
  const [locale, setLocale] = useState<Locale>(DEFAULT_LOCALE);

  useEffect(() => {
    const next = pickClientLocale();
    if (next !== locale) setLocale(next);
    // We intentionally only run this on mount; the picker triggers a hard
    // reload after writing the cookie, so locale never changes mid-session.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <NextIntlClientProvider
      locale={locale}
      messages={getMessages(locale) as Record<string, string>}
      // Keep the timezone deterministic so server + client agree during
      // hydration. Operators that need a different default can override
      // this in a custom build.
      timeZone="UTC"
    >
      {children}
    </NextIntlClientProvider>
  );
}
