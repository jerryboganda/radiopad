/**
 * Iter-35 i18n — locale negotiation middleware.
 *
 * Resolves the active locale on every request from:
 *   1. `?lang=<tag>` query parameter (sets the cookie + redirects without
 *      the param so subsequent visits stick).
 *   2. `radiopad-locale` cookie.
 *   3. `Accept-Language` header.
 *   4. tenant default (read by the layout via the API; not available here).
 *   5. `en`.
 *
 * Note: the production frontend ships as a static export
 * (`output: 'export'` in `next.config.ts`). Static exports do not execute
 * middleware at request time — locale negotiation happens client-side via
 * `frontend/lib/i18n.ts#pickClientLocale`. This middleware is exercised
 * during `next dev` and remains the canonical reference for the priority
 * chain.
 */

import { NextRequest, NextResponse } from 'next/server';
import {
  LOCALE_COOKIE,
  LOCALE_QUERY_PARAM,
  coerceLocale,
  pickFromAcceptLanguage,
  DEFAULT_LOCALE,
} from './lib/i18n';

export function middleware(req: NextRequest): NextResponse {
  const url = req.nextUrl;
  const fromQuery = coerceLocale(url.searchParams.get(LOCALE_QUERY_PARAM));
  if (fromQuery) {
    const next = url.clone();
    next.searchParams.delete(LOCALE_QUERY_PARAM);
    const res = NextResponse.redirect(next);
    res.cookies.set(LOCALE_COOKIE, fromQuery, {
      path: '/',
      maxAge: 60 * 60 * 24 * 365,
      sameSite: 'lax',
    });
    return res;
  }

  const fromCookie = coerceLocale(req.cookies.get(LOCALE_COOKIE)?.value ?? null);
  if (fromCookie) return NextResponse.next();

  const fromAccept = pickFromAcceptLanguage(req.headers.get('accept-language'));
  const res = NextResponse.next();
  if (fromAccept && fromAccept !== DEFAULT_LOCALE) {
    res.cookies.set(LOCALE_COOKIE, fromAccept, {
      path: '/',
      maxAge: 60 * 60 * 24 * 365,
      sameSite: 'lax',
    });
  }
  return res;
}

export const config = {
  // Skip static asset paths and API routes.
  matcher: ['/((?!_next/|api/|favicon.ico|.*\\..*).*)'],
};
