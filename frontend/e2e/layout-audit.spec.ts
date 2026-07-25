import { readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Page } from '@playwright/test';

/**
 * Layout-regression audit.
 *
 * Renders every route and fails on the specific ways RadioPad's UI has broken
 * silently in the past — none of which a build, a typecheck, or a unit test can
 * see, because the markup is valid and the page still returns 200:
 *
 *   1. stacked-icon    — Tailwind preflight sets `svg { display: block }`, so an
 *                        icon in a non-flex container takes its own line and
 *                        drops the label underneath it.
 *   2. clipped-table   — `.table-wrap` is `overflow: clip`; a table wider than
 *                        it loses its last columns with no scrollbar at all.
 *   3. page-overflow-x — the mirror failure: a bare wide table with no scroll
 *                        container drags the whole page sideways.
 *   4. crushed-text    — a wide sibling (segmented control, button group) or an
 *                        unfloored table column squeezes text into a
 *                        one-word-per-line sliver.
 *
 * See docs/02-design/design.md §3.2 and the `.rp-table-scroll` entry for the
 * sanctioned fixes. If you are here because this test failed, the fix is
 * almost never to loosen the assertion.
 */

type Finding = { kind: string; detail: string };

const APP_DIR = join(__dirname, '..', 'app');

/**
 * Routes are read off the filesystem rather than hardcoded, so a new page is
 * covered the day it lands instead of the day someone remembers this file.
 */
function routesIn(group: string): string[] {
  const root = join(APP_DIR, group);
  const out: string[] = [];
  const walk = (dir: string, prefix: string) => {
    let entries: string[];
    try {
      entries = readdirSync(dir);
    } catch {
      return; // group absent in this checkout (surface staging)
    }
    if (entries.includes('page.tsx')) out.push(prefix === '' ? '/' : prefix);
    for (const e of entries) {
      const full = join(dir, e);
      if (!statSync(full).isDirectory()) continue;
      // Dynamic and private segments cannot be visited without real ids.
      if (e.startsWith('[') || e.startsWith('_') || e.startsWith('(')) continue;
      walk(full, `${prefix}/${e}`);
    }
  };
  walk(root, '');
  return out.sort();
}

/** Injected into the page; must stay dependency-free and self-contained. */
function auditInPage(): Finding[] {
  const out: Finding[] = [];
  const skip = (el: Element) =>
    el.closest('.rp-sidebar, .rp-topbar, nav, [class*="sr-only"]') !== null;
  const label = (el: Element) => {
    const t = (el.textContent || '').replace(/\s+/g, ' ').trim().slice(0, 48);
    const cls =
      typeof el.className === 'string' && el.className
        ? '.' + el.className.trim().split(/\s+/).slice(0, 2).join('.')
        : '';
    return `${el.tagName.toLowerCase()}${cls}${t ? ` "${t}"` : ''}`;
  };

  // 1. icon stacked above its label
  for (const el of document.querySelectorAll('button,a,h1,h2,h3,h4,p,span,div,li,td,th')) {
    if (skip(el)) continue;
    const svg = el.querySelector(':scope > svg');
    if (!svg) continue;
    const hasOwnText = [...el.childNodes].some(
      (n) => n.nodeType === 3 && (n.textContent || '').trim(),
    );
    if (!hasOwnText) continue;
    if (/flex|grid/.test(getComputedStyle(el).display)) continue;
    if (getComputedStyle(svg).display === 'block') {
      out.push({ kind: 'stacked-icon', detail: label(el) });
    }
  }

  // 2. table amputated by a clipping wrapper
  for (const el of document.querySelectorAll('*')) {
    if (skip(el)) continue;
    const cs = getComputedStyle(el);
    if (!/clip|hidden/.test(cs.overflowX)) continue;
    if (cs.textOverflow === 'ellipsis') continue; // deliberate truncation
    if (!el.querySelector('table')) continue;
    if (el.scrollWidth > el.clientWidth + 2 && el.clientWidth > 20) {
      out.push({
        kind: 'clipped-table',
        detail: `${label(el)} (${el.scrollWidth} > ${el.clientWidth})`,
      });
    }
  }

  // 3. the page itself scrolls sideways
  const de = document.documentElement;
  if (de.scrollWidth > de.clientWidth + 2) {
    out.push({ kind: 'page-overflow-x', detail: `${de.scrollWidth} > ${de.clientWidth}` });
  }

  // 4. text crushed into a sliver column
  for (const el of document.querySelectorAll('div,p,span,td,label')) {
    if (skip(el) || el.children.length > 2) continue;
    const text = (el.textContent || '').trim();
    if (text.length < 25) continue;
    const r = el.getBoundingClientRect();
    if (!r.width || r.width > 130) continue;
    const lh = parseFloat(getComputedStyle(el).lineHeight) || 16;
    if (r.height > lh * 4) {
      out.push({ kind: 'crushed-text', detail: `${label(el)} (width ${Math.round(r.width)}px)` });
    }
  }

  return out;
}

async function auditRoute(page: Page, route: string, theme: 'light' | 'dark') {
  // Set the preference before first paint so the app boots in the target theme.
  // Forcing `data-theme` after load yields stale computed styles and invents
  // failures that do not exist.
  await page.addInitScript((t) => {
    try {
      window.localStorage.setItem('rp-theme', t as string);
    } catch {
      /* storage unavailable — the run still exercises the default theme */
    }
  }, theme);

  // next.config.ts sets `trailingSlash: true`; visiting without it costs a
  // redirect on every route and can settle after `networkidle` fires.
  const url = route.endsWith('/') ? route : `${route}/`;
  await page.goto(url, { waitUntil: 'networkidle' });
  // Let the shell settle: data fetches resolve into tables, skeletons swap out.
  await page.waitForTimeout(600);

  const resolved = await page.evaluate(() => document.documentElement.dataset.theme ?? 'light');
  expect(
    resolved,
    `expected the app to boot in ${theme}; got ${resolved}. A stale theme makes every ` +
      'contrast/layout reading meaningless, so this is a harness failure, not a UI one.',
  ).toBe(theme);

  return page.evaluate(auditInPage);
}

function report(route: string, theme: string, width: number, findings: Finding[]) {
  const lines = findings.map((f) => `  • [${f.kind}] ${f.detail}`);
  return `${route} @ ${width}px (${theme}) — ${findings.length} layout issue(s):\n${lines.join('\n')}`;
}

// The desktop app ships Windows-only and is never rendered at phone width, so
// it is audited at one representative desktop size. The web surface runs in a
// real browser and the mobile companion is a phone app, so both get narrow runs.
const DESKTOP_ROUTES = [...routesIn('(desktop)'), ...routesIn('(shared)')];
const WEB_ROUTES = routesIn('(web)');
const MOBILE_ROUTES = [...routesIn('(mobile)'), '/login', '/pair'];

const MATRIX: Array<{ name: string; routes: string[]; width: number }> = [
  { name: 'desktop', routes: DESKTOP_ROUTES, width: 1400 },
  { name: 'web', routes: WEB_ROUTES, width: 1400 },
  { name: 'web-tablet', routes: WEB_ROUTES, width: 768 },
  { name: 'web-phone', routes: WEB_ROUTES, width: 375 },
  { name: 'mobile', routes: MOBILE_ROUTES, width: 375 },
];

for (const { name, routes, width } of MATRIX) {
  for (const theme of ['light', 'dark'] as const) {
    test.describe(`${name} @ ${width}px — ${theme}`, () => {
      test.use({ viewport: { width, height: 900 } });

      for (const route of routes) {
        test(`${route}`, async ({ page }) => {
          const findings = await auditRoute(page, route, theme);
          expect(findings, report(route, theme, width, findings)).toEqual([]);
        });
      }
    });
  }
}
