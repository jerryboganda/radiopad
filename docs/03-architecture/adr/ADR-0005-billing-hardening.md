# ADR-0005: Billing & subscription hardening

- **Status:** Accepted
- **Date:** 2026-05-04
- **Last Updated:** 2026-05-04
- **Decision-makers:** Engineering + Product + Security
- **Related PRD:** BILL-002, BILL-003, BILL-004, BILL-005, BILL-007, MKT-006
- **Related code:**
  - `backend/RadioPad.Api/src/RadioPad.Api/Controllers/BillingController.cs`
  - `backend/RadioPad.Api/src/RadioPad.Api/Controllers/MarketplaceController.cs`
  - `backend/RadioPad.Api/src/RadioPad.Application/Services/PlanQuotaService.cs`
  - `backend/RadioPad.Api/src/RadioPad.Application/Services/SubscriptionLifecycleService.cs`
  - `backend/RadioPad.Api/src/RadioPad.Api/Middleware/SuspensionGuardMiddleware.cs`
  - `backend/RadioPad.Api/src/RadioPad.Application/Services/AiGateway.cs`

## Context

The first-pass `BillingController` (Iteration 13) and `MarketplaceController`
(Iteration 25) shipped with the minimum viable Stripe surface: Checkout +
Billing Portal + a single webhook handler reading `STRIPE_SECRET_KEY` /
`STRIPE_WEBHOOK_SECRET` directly. The PRD billing requirements (BILL-002
trial windows, BILL-003 plan-gated AI usage, BILL-004 grace + suspension
lifecycle, BILL-005 invoices, BILL-007 refunds) and the marketplace
requirement (MKT-006 Stripe Connect onboarding gates + dispute handling)
were not yet enforced. In particular:

- Stripe webhooks are at-least-once; without dedup the same event could
  toggle `TenantSettings` twice.
- AI calls were not gated against the active plan, so a Trial tenant could
  burn arbitrary tokens.
- `SuspendedAt` and `GracePeriodUntil` had no enforcement path.
- Refunds and the Connect onboarding state were unreachable from the API.
- Audit rows carried raw `email` / Stripe identifiers into `DetailsJson`.

## Decision

1. **Canonical env scheme.** Stripe secrets read through a new `BillingEnv`
   helper which prefers `RADIOPAD_STRIPE_SECRET_KEY` /
   `RADIOPAD_STRIPE_WEBHOOK_SECRET` and falls back to the legacy
   `STRIPE_*` names for one release.
2. **Idempotency-Keys on every Stripe API call** (Checkout, Billing Portal,
   invoice fetch, refunds, Connect onboarding, marketplace transfer) so
   retries are safe.
3. **Webhook dedup via `StripeWebhookEvents`** (unique `EventId`). Replays
   are accepted with `200 OK` but produce no second mutation and no second
   audit row, preserving append-only semantics on `AuditEvents`.
4. **Plan-gated AI quota** through a new `PlanQuotaService` consulted at
   `AiGateway.RouteAsync`. Exhausted plans throw `QuotaExceededException`,
   translated by global problem-details middleware to
   `402 { kind: "quota_exceeded", resetAt }`.
5. **Suspension guard middleware.** New `SuspensionGuardMiddleware` returns
   `402 { kind: "tenant_suspended", suspendedAt }` on every mutating
   non-billing `/api/*` request when `TenantSettings.SuspendedAt != null`.
   `/api/billing/*` and `/api/auth/*` are exempt so operators can recover.
6. **Connect onboarding gating.** Buyer checkout against a marketplace
   listing returns `409 { kind: "connect_not_ready" }` when the publisher's
   Stripe Connect account has `charges_enabled=false`.
7. **Refund + dispute endpoints.** New `POST /api/billing/refund`,
   `POST /api/marketplace/purchases/{id}/refund`, and webhook handling for
   `charge.dispute.created`.
8. **PII-safe audit.** New `IBillingAudit` hashes `email`,
   `stripeCustomerId`, `paymentIntentId`, and `subscriptionId` to
   `sha16:<hex>` before writing `AuditAction.BillingChanged` rows.

The decision is captured in EF migration `BillingHardening` (new fields on
`TenantSettings`, new `StripeWebhookEvents` entity).

## Consequences

- Operators must migrate environment variables to `RADIOPAD_STRIPE_*` within
  one release. Legacy names continue to work in the meantime; release notes
  call out the deprecation.
- All new behaviour is covered by integration tests under
  `tests/RadioPad.Api.Tests/Integration/` (webhook dedup, plan-quota gate,
  suspension guard, Connect gating, refund happy-path + RBAC, billing-audit
  PII hashing).
- `AiGateway.EnforcePhiPolicy` is unchanged by this ADR. (Since then, its
  PHI routing gate was removed on 2026-07-20 by operator decision; it now
  rejects only disabled and `Blocked` providers.) Plan-quota gating runs
  **after** it, so requests it refuses still audit
  `AuditAction.ProviderBlocked` first.
- The append-only audit invariant is preserved: webhook dedup happens in
  the new `StripeWebhookEvents` table, not by mutating `AuditEvents`.
- Tenant isolation is preserved: every new query filters by the resolved
  `(tenant, user)` tuple from `TenantedController.ResolveContextAsync`.
