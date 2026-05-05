'use client';

/**
 * Iter-35 i18n — translated topbar. Mirrors the static layout used in
 * `frontend/app/layout.tsx` before the i18n switch, but pulls the visible
 * labels from the active message bundle and renders the locale picker
 * after the "Sign in" link.
 */

import Link from 'next/link';
import { useTranslations } from 'next-intl';
import LocalePicker from './LocalePicker';

export default function Topbar() {
  const tNav = useTranslations('nav');
  const tBar = useTranslations('topbar');
  const tSubtle = useTranslations('buttons.subtle');

  return (
    <header className="topbar">
      <div className="topbar-left">
        <span className="brand-mark" aria-hidden>
          <span className="brand-mark-letter">R</span>
        </span>
        <div className="topbar-title">
          <span className="title">{tBar('title')}</span>
          <span className="meta">{tBar('tagline')}</span>
        </div>
      </div>
      <nav className="topbar-right rp-nav" aria-label="Primary">
        <Link href="/">{tNav('reports')}</Link>
        <Link href="/rulebooks">{tNav('rulebooks')}</Link>
        <Link href="/templates">{tNav('templates')}</Link>
        <Link href="/providers">{tNav('providers')}</Link>
        <Link href="/prompts">{tNav('prompts')}</Link>
        <Link href="/marketplace">{tNav('marketplace')}</Link>
        <Link href="/validation">{tNav('validation')}</Link>
        <Link href="/admin/governance">{tNav('governance')}</Link>
        <Link href="/admin/model-eval">{tNav('modelEval')}</Link>
        <Link href="/audit">{tNav('audit')}</Link>
        <Link href="/analytics">{tNav('analytics')}</Link>
        <Link href="/terminology">{tNav('terminology')}</Link>
        <Link href="/offline">{tNav('offline')}</Link>
        <Link href="/admin/settings">{tNav('settings')}</Link>
        <Link href="/admin/billing">{tNav('billing')}</Link>
        <Link href="/admin/usage">{tNav('usage')}</Link>
        <Link href="/admin/feature-flags">{tNav('featureFlags')}</Link>
        <Link href="/admin/fhir-import">{tNav('fhirImport')}</Link>
        <Link href="/admin/pacs">{tNav('pacs')}</Link>
        <Link href="/admin/security">{tNav('security')}</Link>
        <Link href="/login">{tNav('signIn')}</Link>
        <LocalePicker ariaLabel={tSubtle('language')} />
      </nav>
    </header>
  );
}
