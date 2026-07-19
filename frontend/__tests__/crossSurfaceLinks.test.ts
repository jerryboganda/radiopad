// Every internal link must point at a route that ships in the SAME surface as the component
// holding it.
//
// RadioPad builds one codebase into three bundles: scripts/build-surface.mjs physically moves the
// non-target `app/(desktop|web|mobile)/` groups out of `app/` before `next build`. So a link from a
// component that ships on desktop to a route that lives only under `(web)` is not a soft 404 — the
// route does not exist in that bundle at all, and the user lands on the client-side not-found page,
// losing whatever they were doing.
//
// Four of these shipped simultaneously: the topbar profile menu's Settings + Billing items (present
// on virtually every screen), the billing banner's "Resolve in billing" call to action, the login
// page's "Pair a device" button, and three "Open X →" links on the web governance dashboard. Each
// was a plain <Link href="..."> with no surface gate, and each was invisible to type-checking and
// to every existing test, because a route group is a build-time concern rather than a type-level
// one. This test reads the actual route tree and the actual sources, so it fails the moment a new
// one appears.
import { describe, it, expect } from 'vitest';
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join, relative, sep } from 'node:path';

const APP = join(__dirname, '..', 'app');
const COMPONENTS = join(__dirname, '..', 'components');

type Surface = 'desktop' | 'web' | 'mobile';
const SURFACES: Surface[] = ['desktop', 'web', 'mobile'];

function walk(dir: string, out: string[] = []): string[] {
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) walk(full, out);
    else out.push(full);
  }
  return out;
}

/** The route group a path sits in, or null for ungrouped files (which ship everywhere). */
function groupOf(path: string): string | null {
  const m = /\((desktop|web|mobile|shared)\)/.exec(path);
  return m ? m[1] : null;
}

/** Which surfaces a file under app/ ships on: its own group, or all three when shared/ungrouped. */
function appSurfacesOf(path: string): Surface[] {
  const g = groupOf(path);
  if (g === null || g === 'shared') return SURFACES;
  return [g as Surface];
}

// A component's surfaces are decided by who renders it, not by where it sits: components/ has no
// route group. CaseQueue links to /reports (desktop-only) and is correct, because the only path to
// it runs through the desktop report editor. So resolve each component's surfaces by walking the
// reverse-import graph up to app/, and fall back to all-surfaces for anything unreachable.
const allSources = [...walk(APP), ...walk(COMPONENTS)].filter(
  (f) => (f.endsWith('.tsx') || f.endsWith('.ts')) && !f.includes('.test.'),
);

const FRONTEND = join(__dirname, '..');

/** Importers of each component file, keyed by its '@/components/...'-style module path. */
const importers = new Map<string, Set<string>>();
for (const file of allSources) {
  const src = readFileSync(file, 'utf8');
  for (const m of src.matchAll(/from\s+'(@\/components\/[^']+)'/g)) {
    const key = m[1].replace('@/', '');
    if (!importers.has(key)) importers.set(key, new Set());
    importers.get(key)!.add(file);
  }
  // Relative imports between components (./CaseQueue).
  if (file.startsWith(COMPONENTS)) {
    for (const m of src.matchAll(/from\s+'(\.[^']+)'/g)) {
      const dir = file.slice(0, file.lastIndexOf(sep));
      const resolved = relative(FRONTEND, join(dir, m[1])).split(sep).join('/');
      if (!importers.has(resolved)) importers.set(resolved, new Set());
      importers.get(resolved)!.add(file);
    }
  }
}

/** Module key for a component file, without extension: components/reports/CaseQueue */
function moduleKey(file: string): string {
  return relative(FRONTEND, file).split(sep).join('/').replace(/\.tsx?$/, '');
}

function surfacesOf(file: string, seen = new Set<string>()): Surface[] {
  if (file.startsWith(APP)) return appSurfacesOf(file);
  const key = moduleKey(file);
  if (seen.has(key)) return [];
  seen.add(key);

  const parents = importers.get(key) ?? new Set<string>();
  if (parents.size === 0) return SURFACES; // never imported — assume the worst
  const out = new Set<Surface>();
  for (const p of parents) for (const s of surfacesOf(p, seen)) out.add(s);
  return out.size ? [...out] : SURFACES;
}

/** Route path for a page.tsx, e.g. app/(desktop)/reports/new/page.tsx → /reports/new. */
function routeOf(pageFile: string): string {
  const rel = relative(APP, pageFile).split(sep).slice(0, -1);
  const segs = rel.filter((s) => !/^\(.*\)$/.test(s));
  return '/' + segs.join('/');
}

const pageFiles = walk(APP).filter((f) => f.endsWith(`${sep}page.tsx`));

/** route → surfaces it is built into. */
const routeSurfaces = new Map<string, Set<Surface>>();
for (const f of pageFiles) {
  const route = routeOf(f);
  const set = routeSurfaces.get(route) ?? new Set<Surface>();
  for (const s of surfacesOf(f)) set.add(s);
  routeSurfaces.set(route, set);
}

// Paths served by the ASP.NET backend, not by Next — they are never route-group scoped.
const BACKEND_PREFIXES = ['/api/', '/saml/', '/ws/', '/swagger', '/health'];

/** Static in-app targets: href="/x" and router.push('/x'), excluding backend + new-tab links. */
function internalTargets(src: string): string[] {
  const out: string[] = [];
  for (const m of src.matchAll(/<(?:a|Link)\b[^>]*?href="(\/[^"{}]*)"[^>]*?>/gs)) {
    // target="_blank" means a real navigation out of the SPA — typically a backend document.
    if (/target="_blank"/.test(m[0])) continue;
    out.push(m[1]);
  }
  for (const m of src.matchAll(/router\.push\(\s*['"](\/[^'"]*)['"]\s*\)/g)) out.push(m[1]);
  return out.filter((t) => !BACKEND_PREFIXES.some((p) => t.startsWith(p)));
}

/** Does this route exist for `surface`, allowing for dynamic segments? */
function routeExistsOn(target: string, surface: Surface): boolean {
  const clean = target.split(/[?#]/)[0].replace(/\/$/, '') || '/';
  for (const [route, surfaces] of routeSurfaces) {
    if (!surfaces.has(surface)) continue;
    if (route === clean) return true;
    // A dynamic segment ([id]) matches any single non-empty segment.
    if (route.includes('[')) {
      const re = new RegExp('^' + route.replace(/\[[^\]]+\]/g, '[^/]+') + '$');
      if (re.test(clean)) return true;
    }
  }
  return false;
}

describe('cross-surface links', () => {
  const sources = allSources;

  it('has route pages to check (guards the guard)', () => {
    expect(pageFiles.length).toBeGreaterThan(20);
    expect(routeSurfaces.get('/login')?.size).toBe(3); // (shared) ships everywhere
  });

  it('never links to a route absent from the linking component’s own surface', () => {
    const dead: string[] = [];

    for (const file of sources) {
      const src = readFileSync(file, 'utf8');
      // A file that branches on the surface flag is doing the gating deliberately; the regex
      // cannot tell which JSX branch a given href sits in, so trust the explicit check.
      if (/isWebSurface|isDesktopSurface|isMobileSurface|surfaceAllows|SURFACE\b/.test(src)) continue;

      for (const target of internalTargets(src)) {
        for (const surface of surfacesOf(file)) {
          if (!routeExistsOn(target, surface)) {
            dead.push(`${relative(join(__dirname, '..'), file)} → ${target} (missing on ${surface})`);
          }
        }
      }
    }

    expect(dead).toEqual([]);
  });
});
