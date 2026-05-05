# SDK Guide

**Status:** Planned  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

> RadioPad does not ship a first-party SDK in v0.x. Customers integrate via the REST API and the CLI. This document captures the planned SDK surface for Phase 2.

## Planned SDKs

| Language | Package | Purpose |
| --- | --- | --- |
| TypeScript | `@radiopad/sdk` | Server- and browser-side. Wraps the typed API client. |
| .NET | `RadioPad.Client` | For C# integrations and the CLI itself. |
| Python | `radiopad` | For analytics / pipelines. |

## Interim integration

- TypeScript / Node: copy `frontend/lib/api.ts` as a starting point.
- .NET: hand-roll using `HttpClient` and the documented endpoints.
- Python: use any HTTP client (`httpx`, `requests`).

## Auth

- v0.1: pass `X-RadioPad-Tenant` and `X-RadioPad-User` headers.
- Phase 3: pass `Authorization: Bearer <jwt>`.

## Error model

- All SDKs surface RFC-7807 problem details with the `kind` field intact.
- Provider-policy block (`kind: provider_policy`, status 403) raises a dedicated typed exception.

## Versioning

- SDK MAJOR versions track the API public surface.
- Minor versions are additive.
- See [VERSIONING.md](../../VERSIONING.md).

## Stability

- Until the first SDK ships, stability guarantees are documented at the API level only.
- The SDK ships **after** v1.0.0 of the API to avoid churn.

## Out of scope

- A binary FHIR client (use existing FHIR libraries).
- A DICOM SDK — RadioPad does not interact with DICOM at the API layer.
