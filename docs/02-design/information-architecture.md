# Information Architecture

**Status:** Current  ·  **Owner:** Design  ·  **Last Updated:** 2026-05-04

## Top-level navigation

```
RadioPad
├── Dashboard          (/)
├── Report editor      (/reports/:id)
├── Templates          (/templates)
├── Rulebooks          (/rulebooks)
├── Providers          (/providers)
├── Audit              (/audit)
└── Audit verify       (/audit/verify)
```

Navigation lives in `.topbar` only. There is no sidebar; the App Router layout is a `topbar + split` shell.

## Page hierarchy

- **Dashboard** — paginated report list with modality / status / search filters.
- **Report editor** — left pane: section-block composer; right pane: validation panel + AI assist.
- **Templates** — list + modal editor (id/label/placeholder/required per section).
- **Rulebooks** — list + raw YAML editor + validate / approve / deprecate actions.
- **Providers** — list + modal editor (compliance class, secret ref).
- **Audit** — JSON-Lines viewer.
- **Audit verify** — client-side SHA-256 chain recomputation.

## Content structure

| Surface | Body | Chrome |
| --- | --- | --- |
| Reports | `var(--serif)` for narrative; `var(--mono)` for ids/codes. | `var(--sans)` for buttons, badges, chrome. |
| Validation findings | `var(--sans)` body; `var(--mono)` for `ruleId`. | Severity badge with locked colour. |
| AI suggestions | Wrapped in `.ai-mark`; tagged "AI suggestion". | Accept / Dismiss buttons. |

## Visibility by role (planned)

| Role | Dashboard | Editor | Templates | Rulebooks | Providers | Audit |
| --- | --- | --- | --- | --- | --- | --- |
| Owner / Admin | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Radiologist | ✅ | ✅ | ✅ (read) | ✅ (read) | ✅ (read) | ✅ (own) |
| Resident | ✅ | ✅ (no sign) | ✅ (read) | ✅ (read) | ❌ | ✅ (own) |
| Auditor | ✅ (read) | ✅ (read) | ✅ (read) | ✅ (read) | ❌ | ✅ |

In v0.1 every authenticated user is treated as a Radiologist.
