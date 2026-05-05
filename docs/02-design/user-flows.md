# User Flows

**Status:** Current  ·  **Owner:** Design  ·  **Last Updated:** 2026-05-04

## Onboarding (Phase 2 SSO; v0.1 dev mode)

```mermaid
flowchart LR
  A[Open RadioPad] --> B{Tenant header present?}
  B -- yes --> D[Dashboard]
  B -- no --> C[Use 'dev' tenant]
  C --> D
```

## Login (planned, Phase 3)

```mermaid
flowchart LR
  A[Click sign in] --> B[Redirect to OIDC provider]
  B --> C[Callback /auth/callback]
  C --> D[Backend exchanges code for tokens]
  D --> E[Set session cookie]
  E --> F[Dashboard]
```

## Draft & sign a report

```mermaid
flowchart TD
  A[Dashboard] --> B[New report]
  B --> C[Choose modality / body part / template]
  C --> D[Editor]
  D --> E[Edit sections]
  E --> F[Validate]
  F -->|Blockers| E
  F -->|No blockers| G[Ask AI optional]
  G --> H[Accept or edit AI text]
  H --> I[Acknowledge]
  I --> J[Export FHIR text]
  J --> K[Status: Exported]
```

## Admin: approve a rulebook

```mermaid
flowchart LR
  A[Rulebooks] --> B[Edit YAML]
  B --> C[radiopad rulebook validate]
  C --> D[radiopad rulebook test]
  D --> E[Save]
  E --> F[Approve]
  F --> G[Audit RulebookApproved]
```

## Billing (planned)

```mermaid
flowchart LR
  A[Trial signup] --> B[Tenant created]
  B --> C[14-day Starter trial]
  C -->|Card on file| D[Active]
  C -->|No card| E[Free tier]
  D -->|Failed charge| F[past_due]
  F -->|7d| G[Suspended]
  G -->|30d| H[Cancelled]
```

## Error / recovery flows

- **Provider blocked by PHI policy** → `.banner.warn` "Provider not allowed for PHI" with a link to compliance docs.
- **Validation fails** → severity-grouped findings panel; one click to jump to the offending section.
- **Network failure** → toast with `X-Request-Id` so support can correlate.
