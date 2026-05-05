# Customer Onboarding

**Status:** Current  ·  **Owner:** Customer Success + Engineering  ·  **Last Updated:** 2026-05-04

## Hosted SKU (planned)

1. **Tenant provisioning.** Operator creates the tenant; assigns the customer-supplied slug; sends invite to the Owner.
2. **IdP integration (Phase 3).** Customer provides OIDC discovery URL + client id; we register a client; SSO tested.
3. **Provider configuration.** Customer chooses providers and supplies API keys (env-var-only). RadioPad confirms compliance class per provider.
4. **Rulebook & template setup.** Either start from the seeded rulebooks or upload customer-specific ones; CLI golden tests pass.
5. **User invites.** Owner invites Admins, Radiologists, etc.
6. **Smoke test.** Run a full draft → validate → AI Mock → acknowledge → export.
7. **Audit chain check.** `radiopad audit verify --tenant <slug>`.
8. **Go live.** Communicate go-live; on-call awareness for the first week.

## On-prem

1. **Pre-flight.** Customer supplies host / DB / proxy details.
2. **Install.** Docker Compose stack from `deploy/`. Migrations applied.
3. **TLS + reverse proxy.** Customer terminates TLS; we provide a Caddy / nginx sample config.
4. **Provider keys.** Customer provisions env vars in the host's secret manager.
5. **Smoke test.** Same as above.
6. **Operational handover.** Runbook walkthrough; on-call contacts exchanged.

## Onboarding artefacts

- Welcome email with portal links.
- Admin guide ([admin-guide.md](admin-guide.md)).
- User guide ([user-guide.md](user-guide.md)).
- CLI guide ([cli-guide.md](cli-guide.md)).
- Security & privacy summary (extract from [docs/04-security/](../04-security/)).
- BAA (if PHI processing) signed with customer + their AI providers.

## Acceptance criteria

- [ ] Tenant provisioned and reachable.
- [ ] At least one report drafted, validated, signed, exported.
- [ ] Audit chain verified.
- [ ] Provider PHI policy tested (intentional block + allow).
- [ ] Customer Owner can invite users.
- [ ] Customer ops contact knows the runbook & escalation path.

## Time-to-value targets

- Hosted SKU: same-day setup, full smoke within 1 week.
- On-prem: depends on customer infrastructure; typically within 2 weeks.
