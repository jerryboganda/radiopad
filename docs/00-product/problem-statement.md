# Problem Statement

**Status:** Current  ·  **Owner:** Product  ·  **Last Updated:** 2026-05-04

## Core problem

Radiologists spend 30–60% of their reporting time on tasks that are mechanical (typing structured findings), error-prone (laterality flips, missing impression sentences), or repetitive (copying institutional language). Existing dictation/voice tools speed up typing but offer no clinical validation, no AI safety, and no auditable workflow.

## Current alternatives

| Alternative | Limitation |
| --- | --- |
| Dictation (Dragon, M*Modal) | Voice → text only. No rule checks. No AI suggestions. No audit chain. |
| RIS/PACS integrated reporting | Vendor-specific; structured templates rare; AI features behind expensive add-ons; closed ecosystems. |
| Generic chat-LLM copy/paste | Convenient but uncontrolled — leaks PHI to non-compliant providers, no validation, no audit, no signed contract. |
| Manual structured-template editors | Solve consistency but lack AI assistance and quality validation. |

## User pain points

1. **Cognitive overhead:** Reporters re-type institutional macros for every case.
2. **Validation drift:** Missing impression, laterality conflicts, mismatched body part vs. modality slip through.
3. **PHI leakage risk:** Ad-hoc LLM use with consumer accounts.
4. **No audit trail:** Hard to answer "who edited this report when, with which AI assistance?"
5. **Vendor lock-in:** Reports trapped in proprietary RIS formats; no clean FHIR export.

## Business opportunity

- Sub-segment of $4B+ radiology IT market actively seeking AI-assisted reporting that is *safe, auditable, and self-hosted-capable*.
- Open architecture (FHIR export, rulebooks, local AI providers) is unique vs. closed RIS vendors.
- Local and self-hosted provider adapters (`ollama`, `vllm`, `llama-cpp`, tenant-owned OpenAI-compatible endpoints) let a hospital keep AI traffic inside its own network. Note that this is a deployment option the operator chooses, not a control the product enforces: the compliance-class routing gate was removed on 2026-07-20 by operator decision, so PHI reaches any enabled provider and the audit trail is the only remaining evidence of where it went. Any BAA posture is the deploying organisation's to establish.

## Why now

- Open-weights medical LLMs (Llama 3.1 medical fine-tunes, MedGemma, etc.) make on-prem AI viable.
- FHIR R4 is the de facto interop standard.
- Health systems are tightening PHI policies — AI vendors that can evidence where PHI went have a structural advantage. RadioPad's answer is the append-only audit trail rather than a routing control.
- Tauri 2 + Capacitor 6 make a single TypeScript codebase deployable across web/desktop/mobile, lowering go-to-market cost.
