# Status Page Policy

**Status:** Draft (status page launching with hosted SKU)  ·  **Owner:** Ops  ·  **Last Updated:** 2026-05-04

## Purpose

A public-facing page where customers can see platform health, scheduled maintenance, and incident timelines.

## Components

- **API availability** per region.
- **Authentication / SSO** per region (Phase 3).
- **AI provider integrations** (per provider, aggregated; we don't expose customer-specific provider state).
- **Audit verification** — last successful nightly run.

## States

- `Operational` — green.
- `Degraded performance` — amber.
- `Partial outage` — orange.
- `Major outage` — red.
- `Maintenance` — blue.

## Update cadence

| Severity | First update | Cadence | Final update |
| --- | --- | --- | --- |
| SEV-1 | within 15 min | every 30 min | with postmortem link |
| SEV-2 | within 1 hour | every 2 hours | with summary |
| SEV-3 | end-of-day | as warranted | with summary |
| Maintenance | ≥ 5 business days notice | start / mid / end | with summary |

## Content rules

- Plain language; no jargon.
- No PHI, no customer names, no internal hostnames.
- Link to the postmortem once published.
- Acknowledge impact (e.g. "Reports failed to save for some tenants between 14:05 and 14:35 UTC.")

## Subscriptions

- Customers can subscribe to per-component email / RSS.
- Hosted SKU customers receive direct email for incidents that affect them.

## Internal mirror

- An internal channel mirrors the status page so engineers always see the customer-visible state.
