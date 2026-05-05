# C4 — Context

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

## System context

```mermaid
C4Context
  title RadioPad — System Context
  Person(rad, "Radiologist", "Drafts, validates, and signs reports.")
  Person(admin, "Informatics admin", "Manages providers, rulebooks, templates.")
  Person(operator, "Operator", "Deploys and monitors the platform.")

  System(radiopad, "RadioPad", "AI-assisted radiology reporting platform.")

  System_Ext(ai_remote, "Remote AI provider", "Anthropic, Ollama remote, etc.")
  System_Ext(ai_local, "Local AI provider", "Ollama on 127.0.0.1.")
  System_Ext(ehr, "EHR / RIS / PACS", "Receives FHIR DiagnosticReport export.")
  System_Ext(idp, "Identity provider", "OIDC / SSO (Phase 3).")

  Rel(rad, radiopad, "Reports via Web / Desktop / Mobile")
  Rel(admin, radiopad, "Configures via Web / CLI")
  Rel(operator, radiopad, "Operates via CLI / runbooks")

  Rel(radiopad, ai_remote, "Drafts impressions on de-identified text")
  Rel(radiopad, ai_local, "Drafts impressions on PHI-bearing text")
  Rel(radiopad, ehr, "Exports DiagnosticReport (text/json)")
  Rel(radiopad, idp, "Authenticates users (Phase 3)")
```

## Trust boundaries

- The boundary between RadioPad and a **remote AI provider** is a strong trust boundary. PHI never crosses it unless the provider has compliance class `PhiApproved`.
- The boundary between RadioPad and a **local AI provider** is a soft trust boundary; PHI may cross it because compliance class is `LocalOnly` and the data does not leave the tenant network.
- The boundary between RadioPad and the **EHR** is governed by the customer's interoperability contract; today it is one-way export only.
