**Status:** Active  **Owner:** Billing  **Last Updated:** 2026-05-05

# Stripe billing — operator runbook

RadioPad uses Stripe for plan checkout, customer portal, refunds, and the
subscription lifecycle that drives the in-app dunning + suspension banners.
This runbook is the operator surface: env vars, the webhook contract, and the
local-testing recipe. Code lives in
[`BillingController`](../../backend/RadioPad.Api/src/RadioPad.Api/Controllers/BillingController.cs)
and [`SubscriptionLifecycleService`](../../backend/RadioPad.Api/src/RadioPad.Application/Services/SubscriptionLifecycleService.cs);
env-var resolution lives in
[`BillingEnv`](../../backend/RadioPad.Api/src/RadioPad.Application/Services/BillingEnv.cs).

## 1. Environment variables

All keys are read through `BillingEnv`, which prefers the canonical
`RADIOPAD_*` name and falls back to the legacy plain `STRIPE_*` for
back-compat. New deployments must set the canonical names.

| Canonical (preferred) | Legacy fallback | Purpose |
| --- | --- | --- |
| `RADIOPAD_STRIPE_SECRET_KEY` | `STRIPE_SECRET_KEY` | Server-side Stripe API key. Loaded into `StripeConfiguration.ApiKey` at controller construction time. |
| `RADIOPAD_STRIPE_WEBHOOK_SECRET` | `STRIPE_WEBHOOK_SECRET` | HMAC secret used by `EventUtility.ConstructEvent` to verify the `Stripe-Signature` header on every webhook delivery. |
| `RADIOPAD_STRIPE_PRICE_TRIAL` | `STRIPE_PRICE_TRIAL` | Stripe price id for the Trial plan. |
| `RADIOPAD_STRIPE_PRICE_TEAM` | `STRIPE_PRICE_TEAM` | Stripe price id for the Team plan. |
| `RADIOPAD_STRIPE_PRICE_ENTERPRISE` | `STRIPE_PRICE_ENTERPRISE` | Stripe price id for the Enterprise plan. |

> Secrets never appear in logs, JSON responses, or audit rows. Customer ids,
> subscription ids, payment-intent ids and email addresses are hashed to
> `sha16:<hex>` by [`BillingAudit`](../../backend/RadioPad.Api/src/RadioPad.Application/Services/BillingAudit.cs)
> before being written to the audit chain.

## 2. Webhook endpoint

- **URL:** `POST /api/billing/webhook`
- **Auth:** anonymous (signature-verified, not session-bound).
- **Signature header:** `Stripe-Signature` (`t=<unix>,v1=<hmac>`). Validated
  with `EventUtility.ConstructEvent` and `RADIOPAD_STRIPE_WEBHOOK_SECRET`.
  Tolerance: 300 s.
- **Tenant resolution:** the handler looks up
  `TenantSettings.StripeCustomerId == event.data.object.customer` (or
  `client_reference_id` for Checkout). No tenant header is honoured.
- **Idempotency:** every accepted event id is inserted into the
  `StripeWebhookEvents` table inside the same DB transaction as the side
  effects. A second delivery returns `200 { received: true, deduped: true }`
  and performs **no** further state mutation.
- **Errors:**
  - Missing webhook secret → `503 Service Unavailable`.
  - Bad / missing / replayed-too-late signature → `400 Bad Request` with an
    RFC-7807-shaped body (`{ error, kind: "validation" }`).

### Supported events (iter-36)

| Stripe event | Handler effect |
| --- | --- |
| `checkout.session.completed` | Creates / updates `TenantSettings` with `StripeCustomerId`, `StripeSubscriptionId`, sets status `active`, audits `checkout.completed`. |
| `customer.subscription.created` | Maps `Subscription.Status` through `SubscriptionLifecycleService.Apply`, updates `Plan` from `price.metadata.plan`, audits `subscription.updated`. |
| `customer.subscription.updated` | Same as `created`. |
| `customer.subscription.deleted` | Same as `created` (status `canceled` ⇒ `SuspendedAt = utcNow`). |
| `invoice.payment_succeeded` | Clears `GracePeriodUntil` and `SuspendedAt`, sets `StripeSubscriptionStatus = "active"`, audits `invoice.payment_succeeded`. |
| `invoice.payment_failed` | First failure: opens 7-day `GracePeriodUntil`. Failure after grace expired: sets `SuspendedAt = utcNow`. Status set to `past_due`. Audited as `AuditAction.BillingChanged` with details `action = invoice.payment_failed`. |

All other event types are accepted (signature verified, dedupe row written)
and ignored — Stripe's "204-style" contract.

### Dunning state machine

`TenantSettings.GracePeriodUntil` and `TenantSettings.SuspendedAt` are the
two fields the frontend banners watch. They are mutated only via
`SubscriptionLifecycleService` (subscription updates) or
`SubscriptionLifecycleService.MarkPaymentFailed` (invoice failures). A
successful invoice payment is the only thing that clears both.

## 3. Local testing

The Stripe CLI proxies the signed webhook stream to your dev backend:

```pwsh
# 1. Start the API on its default 127.0.0.1:7457 bind
dotnet run --project backend/RadioPad.Api/src/RadioPad.Api

# 2. In another shell, log into Stripe (test mode) and forward
stripe login
stripe listen --forward-to http://localhost:7457/api/billing/webhook
# Copy the displayed `whsec_...` into RADIOPAD_STRIPE_WEBHOOK_SECRET and
# restart the API so the new secret is loaded.

# 3. Trigger a fixture event
stripe trigger invoice.payment_failed
stripe trigger invoice.payment_succeeded
```

Verify the side effects against the dev SQLite database:

```pwsh
sqlite3 backend/RadioPad.Api/src/RadioPad.Api/radiopad.db `
  "SELECT TenantId, StripeSubscriptionStatus, GracePeriodUntil, SuspendedAt FROM TenantSettings;"
sqlite3 backend/RadioPad.Api/src/RadioPad.Api/radiopad.db `
  "SELECT EventId, EventType FROM StripeWebhookEvents ORDER BY rowid DESC LIMIT 10;"
```

## 4. Production checklist

- Webhook endpoint is reachable from Stripe's egress IPs (TLS-terminated by
  the operator's reverse proxy; backend itself binds `127.0.0.1`).
- `RADIOPAD_STRIPE_WEBHOOK_SECRET` rotated through Stripe Dashboard → API
  → Webhooks; ship the new secret to the API host before flipping the
  Dashboard secret to avoid a verification gap.
- Alert on a sustained spike of `400` responses from `/api/billing/webhook`
  (signature drift) or on rows in the audit log with
  `AuditAction.BillingChanged` with details `action = invoice.payment_failed` for any single tenant > 2 in 14 days.
- Stripe is the only external billing subprocessor — see
  [security-architecture.md](../04-security/security-architecture.md#vendor-subprocessors).
