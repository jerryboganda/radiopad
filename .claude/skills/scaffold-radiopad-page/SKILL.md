---
name: scaffold-radiopad-page
description: Use when creating a new RadioPad page or top-level component so it follows the mandatory RC shell contract — correct surface route group, AppShell + Container + PageHeader, tokens-only styling with documented .rp-* classes, .ai-mark on AI text, and Skeleton/EmptyState/ErrorState for data states.
---

# Scaffold a RadioPad page

RadioPad UI has a **non-negotiable composition contract** (CLAUDE.md hard rule 3, `docs/02-design/design.md`). Follow it exactly; read `docs/02-design/design.md` before non-trivial UI work.

## 1. Pick the surface + route group

Routes live under `frontend/app/(desktop|web|mobile|shared)/`. Choose by audience:

- **desktop** — the reporting product (worklist, editor, dictation, library, settings, companion host). Clinical roles.
- **web** — master-admin / platform ops only (`admin/*`, users, billing, SSO, providers, governance, usage). No reporting.
- **mobile** — dictation companion (pairing + voice + remote) only.
- **shared** — used by more than one surface.

`scripts/build-surface.mjs` physically stages non-target groups out of `app/` per build, so placement decides which bundle ships the route. Tag nav entries in `components/shell/nav.config.tsx`.

## 2. Wrap the page

Every page renders inside the shell. Minimal skeleton:

```tsx
import { AppShell } from '@/components/shell/AppShell';
import { Container } from '@/components/shell/Container';
import { PageHeader } from '@/components/shell/PageHeader';

export default function Page() {
  return (
    <AppShell>
      <Container>
        <PageHeader title="…" />
        <section className="rp-panel">{/* … */}</section>
      </Container>
    </AppShell>
  );
}
```

Reference the real props of `components/shell/{AppShell,Container,PageHeader,nav.config}.tsx` — do not invent props.

## 3. Style with tokens + documented classes only

- **No hardcoded colours** — no hex/rgb/hsl in TSX or feature CSS. Use the alias contract (`--bg`, `--accent`, `--accent-fg`, semantic families) from `frontend/app/tokens.css`, or Tailwind scales that resolve to those vars.
- Use only the documented classes: `.rp-panel`, `.section-block`, `.composer`, `.msg`, `.finding`, `.ai-mark`, `.badge`, `.status-badge`, button variants `.primary` / `.primary-ghost` / `.ghost` / `.subtle`, plus the shared classes in `tokens.css`.
- Icons: `lucide-react`. **No emoji as icons.** No MUI/Ant/Chakra/Bootstrap.

## 4. AI text + validation severities

- Any AI-generated text wears `.ai-mark` (blue "✨ generated" tinted field + label) **paired with an amber "Requires review" flag** — never hue-only — until acknowledged or edited.
- Validation severities map to fixed colours: Blocker → red, Warning → amber, Info/Style → blue.

## 5. Data states

Data-driven pages must handle all three: `<Skeleton />` (loading), `<EmptyState />` (empty), `<ErrorState onRetry />` (error).

## 6. Verify

Check the page in **both** light and dark themes before calling it done (toggle `html[data-theme]` / the `<ThemeToggle />`). Print/export always renders the light document theme. If the change ships in the desktop bundle, remember DESK-001 (`pnpm release:desktop`).
