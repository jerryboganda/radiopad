# FAQ

**Status:** Current  ·  **Owner:** Product + Support  ·  **Last Updated:** 2026-05-04

## Product

**Q: Does RadioPad sign reports automatically?**
A: No. Sign-off is always a human action. AI never auto-signs.

**Q: Can the AI choose the rulebook for me?**
A: No. Rulebook selection is a human decision.

**Q: Why is the AI text purple?**
A: AI-drafted content wears the `.ai-mark` (purple family) until you acknowledge it.

**Q: My PHI request is blocked. Why?**
A: The selected provider's compliance class isn't permitted to receive PHI. Pick a `PhiApproved` or `LocalOnly` provider, or de-identify the input.

**Q: Where do I see who signed a report?**
A: The audit log on the Audit page; the `ReportAcknowledged` event records the user and time.

## Operations

**Q: How do I verify the audit chain?**
A: `radiopad audit verify --tenant <slug>`. Exit code 0 means the chain is intact.

**Q: Can I roll back a database migration?**
A: We do forward-only migrations. To "rollback" you author a new migration that restores the prior shape.

**Q: Where do API keys live?**
A: Env vars referenced by `ApiKeySecretRef = "env:NAME"`. They never appear in the database, logs, or responses.

**Q: How do I add a new AI provider?**
A: Implement `IProviderAdapter`, add a row in the provider catalog, run the safety eval, document in [model-policy.md](../01-ai-agent/model-policy.md). See [model-abstraction.md](../05-data-ai/model-abstraction.md).

## Compliance

**Q: Is RadioPad HIPAA compliant?**
A: Architecture is HIPAA-compatible. Compliance is a deployment exercise — see [compliance-matrix.md](../04-security/compliance-matrix.md).

**Q: Is patient data sent to AI providers?**
A: Only when (a) the request is flagged `containsPhi: true` AND (b) the chosen provider's compliance class is `PhiApproved` or `LocalOnly`. Otherwise the gateway blocks the request.

**Q: Where is data stored?**
A: In your deployment's database. Hosted: managed PostgreSQL. On-prem: your customer-managed Postgres. We don't operate a multi-customer database in v0.x.

## Development

**Q: Can I add Tailwind?**
A: No. The UI is locked to the Open Design tokens + component classes. See [design.md](../02-design/design.md).

**Q: Can I add Express to the backend?**
A: No. Backend is ASP.NET Core 8. New frameworks require an ADR + human review.

**Q: How do I run the test suite?**
A: `dotnet test` (backend) and `pnpm typecheck` + `pnpm test` (frontend).
