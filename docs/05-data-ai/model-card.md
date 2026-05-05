# Model Card

**Status:** Skeleton (per-provider cards live with the provider catalog)  ·  **Owner:** AI  ·  **Last Updated:** 2026-05-04

> RadioPad does not train its own model. This card documents how we describe the **third-party models** routed through RadioPad and the lens through which we evaluate them.

## Per-provider model card template

Each entry in [provider-catalog.md](../03-architecture/provider-catalog.md) is paired with a short card containing:

- **Model name and version** (e.g. Claude Sonnet 4 / Anthropic).
- **Hosted by:** Anthropic / OpenAI / customer / etc.
- **Compliance class:** `Sandbox / DeIdentifiedOnly / PhiApproved / LocalOnly`.
- **Allowed inputs:** modality / body part / PHI flag matrix.
- **Out of scope:** image interpretation; auto-sign; clinical recommendations beyond the prompt.
- **Known limitations:** rate of hallucination on long findings; weak on niche modalities; etc.
- **Eval results:** link to the latest eval run for this provider.
- **Training data disclosure:** whether the vendor has published a model card; link.
- **Date evaluated by RadioPad:** YYYY-MM-DD.
- **Reviewer:** name / role.

## How to add a new provider

1. Add the provider row in DB.
2. Run the safety + accuracy + tone evals.
3. Author the model card here.
4. Publish a CHANGELOG note in `[Unreleased] / Added`.

## Mock provider

- **Purpose:** Deterministic responses for development and tests.
- **Compliance class:** `LocalOnly` (the response is local; the input does not leave the process).
- **Outputs:** scripted; never used in clinical decisions.

## Ollama (local)

- **Purpose:** PHI-bearing input on-prem.
- **Compliance class:** `LocalOnly`.
- **Models:** customer-selected; small instruction-tuned models for impression drafting.
- **Notes:** quality varies sharply by model; eval before approving for clinical use.

## Anthropic (Claude family)

- **Purpose:** High-quality remote drafting.
- **Compliance class:** `DeIdentifiedOnly` by default; `PhiApproved` only after the customer signs a BAA with Anthropic and updates the provider compliance class.
