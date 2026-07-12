/**
 * Build-time surface flag — which RadioPad shell this bundle is built for.
 *
 * RadioPad ships one frontend codebase as three specialised surfaces:
 *   - `desktop` — the full radiology reporting product (Tauri shell + local API).
 *   - `web`     — master-admin / platform operations only (browser, cloud API).
 *   - `mobile`  — dictation companion that pairs to a live desktop (Capacitor).
 *
 * The active surface is selected at build time via the `RADIOPAD_SURFACE`
 * environment variable (set by `frontend/scripts/build-surface.mjs`) and
 * inlined into the client bundle by the `env` block in `next.config.ts`. It
 * defaults to `desktop`, so a plain `next dev` / `next build` behaves like the
 * full application.
 *
 * This is UX / packaging scoping only. The backend still enforces RBAC and
 * tenant isolation — a narrower surface never widens what the server allows.
 */

export type Surface = 'desktop' | 'web' | 'mobile';

/** All surfaces, in canonical order. */
export const SURFACES: readonly Surface[] = ['desktop', 'web', 'mobile'] as const;

// These booleans compare directly against the value Next inlines for
// `process.env.RADIOPAD_SURFACE`, so each folds to a literal `true`/`false` at
// build time. That lets webpack dead-code-eliminate off-surface branches — e.g.
// the reporting dashboard behind `if (isDesktopSurface)` is physically dropped
// from the web/mobile bundles, not merely hidden. Do NOT route these through a
// helper function; a function call would defeat the constant folding.
export const isWebSurface = process.env.RADIOPAD_SURFACE === 'web';
export const isMobileSurface = process.env.RADIOPAD_SURFACE === 'mobile';
export const isDesktopSurface = !isWebSurface && !isMobileSurface;

/**
 * The surface this bundle was built for. Constant for the lifetime of the
 * build — inlined by Next's `env` config, so it is safe to branch on in both
 * server and client components of the static export.
 */
export const SURFACE: Surface = isWebSurface ? 'web' : isMobileSurface ? 'mobile' : 'desktop';

/**
 * Whether something tagged with `surfaces` belongs on the active surface. An
 * omitted or empty tag means "all surfaces" (shared). Used to scope navigation
 * items and, later, route/interstitial gating.
 */
export function surfaceAllows(surfaces?: readonly Surface[] | null): boolean {
  if (!surfaces || surfaces.length === 0) return true;
  return surfaces.includes(SURFACE);
}
