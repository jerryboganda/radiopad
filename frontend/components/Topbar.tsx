'use client';

/**
 * Iter-35 i18n — translated topbar. Mirrors the static layout used in
 * `frontend/app/layout.tsx` before the i18n switch, but pulls the visible
 * labels from the active message bundle and renders the locale picker
 * after the "Sign in" link.
 */

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { useTranslations } from 'next-intl';
import LocalePicker from './LocalePicker';

export default function Topbar() {
  const tNav = useTranslations('nav');
  const tBar = useTranslations('topbar');
  const tSubtle = useTranslations('buttons.subtle');
  const pathname = usePathname();

  function navClass(href: string) {
    return pathname === href || pathname.startsWith(href + '/') ? 'active' : '';
  }

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
        <Link href="/" className={navClass('/')}>{tNav('reports')}</Link>
        <Link href="/rulebooks" className={navClass('/rulebooks')}>{tNav('rulebooks')}</Link>
        <Link href="/templates" className={navClass('/templates')}>{tNav('templates')}</Link>
        <Link href="/providers" className={navClass('/providers')}>{tNav('providers')}</Link>
        <Link href="/prompts" className={navClass('/prompts')}>{tNav('prompts')}</Link>
        <Link href="/marketplace" className={navClass('/marketplace')}>{tNav('marketplace')}</Link>
        <Link href="/validation" className={navClass('/validation')}>{tNav('validation')}</Link>
        <Link href="/admin/governance" className={navClass('/admin/governance')}>{tNav('governance')}</Link>
        <Link href="/admin/model-eval" className={navClass('/admin/model-eval')}>{tNav('modelEval')}</Link>
        <Link href="/audit" className={navClass('/audit')}>{tNav('audit')}</Link>
        <Link href="/analytics" className={navClass('/analytics')}>{tNav('analytics')}</Link>
        <Link href="/terminology" className={navClass('/terminology')}>{tNav('terminology')}</Link>
        <Link href="/offline" className={navClass('/offline')}>{tNav('offline')}</Link>
        <Link href="/admin/settings" className={navClass('/admin/settings')}>{tNav('settings')}</Link>
        <Link href="/admin/billing" className={navClass('/admin/billing')}>{tNav('billing')}</Link>
        <Link href="/admin/usage" className={navClass('/admin/usage')}>{tNav('usage')}</Link>
        <Link href="/admin/feature-flags" className={navClass('/admin/feature-flags')}>{tNav('featureFlags')}</Link>
        <Link href="/admin/fhir-import" className={navClass('/admin/fhir-import')}>{tNav('fhirImport')}</Link>
        <Link href="/admin/pacs" className={navClass('/admin/pacs')}>{tNav('pacs')}</Link>
        <Link href="/admin/security" className={navClass('/admin/security')}>{tNav('security')}</Link>
        <Link href="/login" className={navClass('/login')}>{tNav('signIn')}</Link>
        <LocalePicker ariaLabel={tSubtle('language')} />
      </nav>
    </header>
  );
}
