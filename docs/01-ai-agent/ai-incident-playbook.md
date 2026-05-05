# AI Incident Playbook

**Status:** Current  ·  **Owner:** Engineering + Clinical  ·  **Last Updated:** 2026-05-04

Triage and response steps for AI-specific incidents. General incident response = [../04-security/incident-response.md](../04-security/incident-response.md).

## Severities

- **SEV-1** — PHI leakage, clinical-safety regression, or audit-chain corruption.
- **SEV-2** — Persistent unsafe outputs, cost-spike > 10× baseline, model outage > 30 min affecting > 1 tenant.
- **SEV-3** — Quality regression below rubric thresholds; isolated complaints.

## Hallucinated output (SEV-2 → SEV-1 if reaches sign-off)

1. Capture the report id, prompt version, model, audit `AiRequest`/`AiResponse` ids.
2. Verify the radiologist did not sign the hallucinated text. If they did → SEV-1: contact the tenant's clinical lead.
3. Add a regression case to [prompt-evals.md](../05-data-ai/prompt-evals.md).
4. If recurring → roll the prompt back via [prompt-versioning.md](prompt-versioning.md).
5. Postmortem within 5 business days.

## Unsafe output (SEV-2)

1. Same capture as above.
2. Update [../05-data-ai/ai-safety.md](../05-data-ai/ai-safety.md) with the new pattern.
3. Patch the system prompt to refuse this class explicitly.
4. Re-run safety evals.

## Data leakage (SEV-1)

1. **Stop the bleed.** Disable the offending provider in the affected tenant.
2. Verify the audit trail; identify the scope (which reports, which user).
3. Notify the tenant per their incident response addendum.
4. Notify legal/compliance; trigger DPA / BAA breach clauses.
5. Postmortem within 3 business days; published to the tenant.

## Incorrect generated reports (SEV-2)

1. Identify the report ids and the rulebook + prompt versions involved.
2. Open a clinical-safety ticket; clinical reviewer assesses real-world impact.
3. If real-world impact possible, escalate to SEV-1.
4. Patch the prompt or rulebook; ship a fix release.

## Cost spike (SEV-2)

1. Check rate-limit metrics and per-tenant token usage.
2. Engage the rate limiter; suspend the offending tenant if abuse is suspected.
3. Add an alert threshold so the next spike notifies on-call earlier.

## Prompt injection (SEV-1 if data exfiltration succeeded)

1. Capture the offending input; redact any PHI before storing.
2. Review the model's response for any tool/credential leak.
3. Patch the system prompt with explicit refusal of injected instructions.
4. Add a regression case to safety evals.

## Model outage (SEV-2)

1. Verify across tenants whether the outage is provider-wide.
2. Surface the outage on the in-app status banner (locked `.banner.warn`).
3. If the provider is the only `PhiApproved` option for a tenant, do **not** silently fall back.
4. Postmortem if the outage exceeds the SLO error budget.
