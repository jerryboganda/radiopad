/**
 * Iter-35 i18n — locale negotiation + message-bundle loading.
 *
 * Priority chain (matches `frontend/middleware.ts` and the spec in
 * `docs/02-design/design.md`):
 *
 *   1. `?lang=<tag>` query parameter
 *   2. `radiopad-locale` cookie
 *   3. `Accept-Language` header (best-of)
 *   4. tenant default (resolved at runtime via `/api/tenant/settings/locale`
 *      and cached on `localStorage` under `radiopad.tenant.locale`)
 *   5. `en`
 *
 * Affects chrome only — clinical content (rulebooks, finding text,
 * validation messages emitted by the rulebook engine) is never
 * translated; the bundles deliberately omit those keys.
 */

import enMessages from '@/messages/en.json';
import esMessages from '@/messages/es.json';
import deMessages from '@/messages/de.json';
import frMessages from '@/messages/fr.json';
import ptMessages from '@/messages/pt.json';
import hiMessages from '@/messages/hi.json';

export const SUPPORTED_LOCALES = ['en', 'es', 'de', 'fr', 'pt', 'hi'] as const;
export type Locale = (typeof SUPPORTED_LOCALES)[number];
export const DEFAULT_LOCALE: Locale = 'en';
export const LOCALE_COOKIE = 'radiopad-locale';
export const LOCALE_QUERY_PARAM = 'lang';
export const TENANT_LOCALE_CACHE_KEY = 'radiopad.tenant.locale';

const BUNDLES: Record<Locale, Record<string, unknown>> = {
  en: enMessages as Record<string, unknown>,
  es: esMessages as Record<string, unknown>,
  de: deMessages as Record<string, unknown>,
  fr: frMessages as Record<string, unknown>,
  pt: ptMessages as Record<string, unknown>,
  hi: hiMessages as Record<string, unknown>,
};

export function isSupportedLocale(value: unknown): value is Locale {
  return typeof value === 'string' && (SUPPORTED_LOCALES as readonly string[]).includes(value);
}

/** Normalise an arbitrary IETF tag to a supported locale or `null`. */
export function coerceLocale(raw: string | null | undefined): Locale | null {
  if (!raw) return null;
  const lower = raw.trim().toLowerCase();
  if (isSupportedLocale(lower)) return lower;
  // `es-MX` → `es`, `pt-BR` → `pt`, `fr-CA` → `fr`, etc.
  const base = lower.split(/[-_]/, 1)[0];
  return isSupportedLocale(base) ? base : null;
}

/** Pick the best supported locale from an `Accept-Language` header value. */
export function pickFromAcceptLanguage(header: string | null | undefined): Locale | null {
  if (!header) return null;
  const parts = header
    .split(',')
    .map((p) => {
      const [tag, ...params] = p.trim().split(';');
      const q = params
        .map((s) => s.trim())
        .find((s) => s.startsWith('q='));
      const quality = q ? Number(q.slice(2)) : 1;
      return { tag: tag.trim(), q: Number.isFinite(quality) ? quality : 0 };
    })
    .filter((p) => p.tag && p.q > 0)
    .sort((a, b) => b.q - a.q);
  for (const { tag } of parts) {
    const match = coerceLocale(tag);
    if (match) return match;
  }
  return null;
}

export function getMessages(locale: Locale): Record<string, unknown> {
  return BUNDLES[locale] ?? BUNDLES[DEFAULT_LOCALE];
}

/**
 * Resolve the active locale from the user's environment. Pure client-side;
 * server components should call `pickServerLocale` instead.
 */
export function pickClientLocale(): Locale {
  if (typeof window === 'undefined') return DEFAULT_LOCALE;
  try {
    const url = new URL(window.location.href);
    const fromQuery = coerceLocale(url.searchParams.get(LOCALE_QUERY_PARAM));
    if (fromQuery) return fromQuery;
    const cookie = readCookie(LOCALE_COOKIE);
    const fromCookie = coerceLocale(cookie);
    if (fromCookie) return fromCookie;
    const fromAccept = pickFromAcceptLanguage(navigator.language || null);
    if (fromAccept) return fromAccept;
    const fromTenant = coerceLocale(window.localStorage.getItem(TENANT_LOCALE_CACHE_KEY));
    if (fromTenant) return fromTenant;
  } catch {
    /* fall through */
  }
  return DEFAULT_LOCALE;
}

/**
 * Resolve the active locale on the server. `tenantDefault` is the locale
 * persisted on `TenantSettings.Locale`; pass `null` if the API has not yet
 * been queried (the function will skip step 4).
 */
export function pickServerLocale(input: {
  searchParams?: Record<string, string | string[] | undefined> | URLSearchParams;
  cookieValue?: string | null;
  acceptLanguage?: string | null;
  tenantDefault?: string | null;
}): Locale {
  const sp = input.searchParams;
  let queryLang: string | null | undefined;
  if (sp instanceof URLSearchParams) {
    queryLang = sp.get(LOCALE_QUERY_PARAM);
  } else if (sp && typeof sp === 'object') {
    const raw = sp[LOCALE_QUERY_PARAM];
    queryLang = Array.isArray(raw) ? raw[0] : raw;
  }
  return (
    coerceLocale(queryLang) ??
    coerceLocale(input.cookieValue) ??
    pickFromAcceptLanguage(input.acceptLanguage) ??
    coerceLocale(input.tenantDefault) ??
    DEFAULT_LOCALE
  );
}

function readCookie(name: string): string | null {
  if (typeof document === 'undefined') return null;
  const target = `${name}=`;
  for (const part of document.cookie.split(';')) {
    const trimmed = part.trim();
    if (trimmed.startsWith(target)) {
      return decodeURIComponent(trimmed.slice(target.length));
    }
  }
  return null;
}

export function writeLocaleCookie(locale: Locale): void {
  if (typeof document === 'undefined') return;
  // 365 days, root path; not HttpOnly — the locale picker is client-driven
  // and contains no secret material.
  const expires = new Date(Date.now() + 365 * 24 * 3600 * 1000).toUTCString();
  document.cookie = `${LOCALE_COOKIE}=${encodeURIComponent(locale)}; Expires=${expires}; Path=/; SameSite=Lax`;
}

/**
 * Walk a dotted key (e.g. `nav.reports`) through the loaded message bundle.
 * Falls back to the English bundle when a key is missing in the active
 * locale; falls back to the key itself if even English doesn't have it.
 */
export function translate(messages: Record<string, unknown>, key: string): string {
  const fromActive = walk(messages, key);
  if (typeof fromActive === 'string') return fromActive;
  const fromEnglish = walk(BUNDLES.en, key);
  if (typeof fromEnglish === 'string') return fromEnglish;
  return key;
}

function walk(node: unknown, key: string): unknown {
  const parts = key.split('.');
  let cursor: unknown = node;
  for (const part of parts) {
    if (cursor && typeof cursor === 'object' && part in (cursor as Record<string, unknown>)) {
      cursor = (cursor as Record<string, unknown>)[part];
    } else {
      return undefined;
    }
  }
  return cursor;
}
