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
- BAA-friendly architecture (`PhiApproved` / `LocalOnly` provider routing) opens hospital deployments without legal blockers.

## Why now

- Open-weights medical LLMs (Llama 3.1 medical fine-tunes, MedGemma, etc.) make on-prem AI viable.
- FHIR R4 is the de facto interop standard.
- Health systems are tightening PHI policies — AI vendors that can demonstrate provable routing controls have a structural advantage.
- Tauri 2 + Capacitor 6 make a single TypeScript codebase deployable across web/desktop/mobile, lowering go-to-market cost.
