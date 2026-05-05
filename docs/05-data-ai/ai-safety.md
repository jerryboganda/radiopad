# AI Safety

**Status:** Current  ·  **Owner:** AI + Clinical  ·  **Last Updated:** 2026-05-04

## Pillars

1. **Human-in-the-loop sign-off** — see [human-in-the-loop.md](human-in-the-loop.md). Non-negotiable.
2. **PHI policy** — see [model-policy.md](../01-ai-agent/model-policy.md) and the gateway. Provider compliance class enforces routing.
3. **Visual marking** — `.ai-mark` until reviewed.
4. **Audit log** — every AI call bracketed with `AiRequest` / `AiResponse`.
5. **Refusal patterns** in the system prompt.
6. **Safety evals** in CI — see [prompt-evals.md](prompt-evals.md).

## Forbidden behaviours

- Auto-signing.
- Choosing or changing the rulebook on the user's behalf.
- Re-disclosing PHI that was redacted upstream.
- Producing a definitive diagnosis without supporting findings.
- Producing dosing or specific medication-change recommendations.
- Image interpretation in v0.x (no image inputs reach AI).

## Bias monitoring

- Tracking quality across modality / body part / patient age band (anonymised).
- Per-cohort acceptance and hallucination rates reviewed quarterly.
- Findings logged to a clinical review meeting; corrective prompts/rulebook changes follow.

## Misuse mitigations

- Rate limit on AI endpoint group.
- PHI policy block visible to admins.
- `ProviderBlocked` audit count alert (planned).

## Reporting issues

- Customers report AI safety issues via the support channel.
- Internal classification: SEV-1 if clinical safety, SEV-2 otherwise.
- Tracked through to a regression test and a CHANGELOG note.

## Yearly safety review

- Re-run the full safety eval set against every approved provider.
- Re-evaluate the prompt library wording against the latest threat models.
- Publish a short safety report (planned for Phase 3).
