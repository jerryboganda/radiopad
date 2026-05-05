# Pricing & Billing

**Status:** Draft  ·  **Owner:** Product  ·  **Last Updated:** 2026-05-04

> Pricing numbers below are placeholders for planning. Real prices are decided by Sales & Finance closer to GA.

## Tiers

| Tier | Audience | Capacity | Indicative price |
| --- | --- | --- | --- |
| **Free / Trial** | Single radiologist, evaluation. | 1 user, 30 reports/month, Mock provider only. | $0 |
| **Starter** | Small practice. | 5 users, unlimited reports, Anthropic/Ollama. | $X/user/mo |
| **Pro** | Mid-size group. | 25 users, custom rulebooks, FHIR endpoint. | $Y/user/mo |
| **Enterprise (Hosted)** | Health systems. | Unlimited users, SSO, audit export, SLA. | Custom |
| **Enterprise (On-prem)** | PHI-sensitive. | Self-hosted, BAA-friendly, support contract. | Annual license |

## Feature gates

| Feature | Free | Starter | Pro | Enterprise |
| --- | --- | --- | --- | --- |
| Locked UI / draft / sign | ✅ | ✅ | ✅ | ✅ |
| Five seed rulebooks | ✅ | ✅ | ✅ | ✅ |
| Custom rulebooks | ❌ | ❌ | ✅ | ✅ |
| AI providers (Mock) | ✅ | ✅ | ✅ | ✅ |
| AI providers (Anthropic / Ollama) | ❌ | ✅ | ✅ | ✅ |
| AI providers (customer BYO PHI-approved) | ❌ | ❌ | ✅ | ✅ |
| Audit export + verify | ❌ | Read-only | ✅ | ✅ |
| FHIR export | ❌ | Text | Text + JSON | Text + JSON |
| SSO / OIDC | ❌ | ❌ | ❌ | ✅ |
| Tenant data residency | Hosted US | Hosted US | Hosted US/EU | Self-hosted |

## Trial logic

- 14-day Starter trial activated on tenant signup.
- Trial converts to Free if no card on file at expiry.
- All audit history is preserved across trial → Free → paid transitions.

## Subscription states

```
trial → active → past_due → suspended → cancelled
                    ↑__renewed______________|
```

- `past_due` after 1 failed charge (3-day dunning).
- `suspended` after 7 days `past_due` — read-only access; signing disabled.
- `cancelled` after 30 days suspended; data exportable for 90 days then purged per [../04-security/data-retention.md](../04-security/data-retention.md).

## Payment provider

- **Planned:** Stripe Billing for hosted SKUs.
- **On-prem:** annual invoice / PO; payment provider is the customer's procurement process.

## Receipts / invoices

- Stripe-generated PDF; tenant admins can download from `/billing`.
- VAT / tax handled by Stripe Tax.
