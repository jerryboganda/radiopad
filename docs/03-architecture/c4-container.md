# C4 — Container

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

```mermaid
C4Container
  title RadioPad — Container view
  Person(rad, "Radiologist")

  System_Boundary(radiopad, "RadioPad") {
    Container(web, "Web app", "Next.js 16 / React 18", "Static export served by Next.js dev server / hosted CDN.")
    Container(desktop, "Desktop shell", "Tauri 2 / Rust", "Wraps the static export; adds global shortcut + clipboard TTL.")
    Container(mobile, "Mobile shell", "Capacitor 6", "Read/acknowledge only.")
    Container(cli, "CLI", ".NET 8 global tool", "Operator + admin tasks.")
    Container(api, "API", "ASP.NET Core 8", "REST + JSON; tenant-aware; audits to AuditLog.")
    ContainerDb(db, "Primary DB", "SQLite (dev) / PostgreSQL (prod)", "Reports, versions, rulebooks, templates, providers, audit chain.")
  }

  System_Ext(ai_remote, "Remote AI")
  System_Ext(ai_local, "Local AI (Ollama)")
  System_Ext(ehr, "EHR / RIS")
  System_Ext(idp, "OIDC IdP (Phase 3)")

  Rel(rad, web, "Uses", "HTTPS")
  Rel(rad, desktop, "Uses")
  Rel(rad, mobile, "Uses (read)")
  Rel(rad, cli, "Operator tasks", "stdio")
  Rel(web, api, "X-RadioPad-Tenant + JSON", "HTTPS")
  Rel(desktop, api, "JSON", "HTTPS")
  Rel(mobile, api, "JSON", "HTTPS")
  Rel(cli, api, "JSON", "HTTPS")
  Rel(api, db, "EF Core")
  Rel(api, ai_remote, "AiGateway", "HTTPS")
  Rel(api, ai_local, "AiGateway", "HTTP loopback")
  Rel(api, ehr, "FHIR export")
  Rel(api, idp, "OIDC (Phase 3)")
```

## Notes

- Web/Desktop/Mobile share the same static export.
- The CLI is privileged-by-headers, not by network — same `X-RadioPad-Tenant` and `X-RadioPad-User` rules apply.
- The DB is single-tenant logically (`TenantId` column) but a single physical instance per deployment.
