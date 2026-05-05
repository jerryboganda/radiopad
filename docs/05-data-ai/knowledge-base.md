# Knowledge Base

**Status:** Planned (Phase 2)  ·  **Owner:** AI + Clinical  ·  **Last Updated:** 2026-05-04

## Sources

| Source | Owner | Format |
| --- | --- | --- |
| Rulebook narrative blocks | Engineering / Clinical | YAML inline `description` fields |
| Tenant protocol PDFs | Tenant admin | Uploaded PDF |
| Internal style guide | RadioPad team | Markdown in repo |
| Public guideline summaries (curated) | RadioPad team | Markdown |
| Tenant glossary (synonyms / preferences) | Tenant admin | JSON |

## Curation

- Public guideline summaries are reviewed by a clinical advisor before inclusion.
- Tenant-uploaded sources are private to that tenant.
- Source files carry metadata: `title`, `version`, `effectiveFrom`, `clinicalReviewer`, `licence` (for redistribution).

## Update flow

1. Admin (or RadioPad team) uploads / edits a source.
2. RAG indexer re-embeds the affected source.
3. An audit event is written (`KbUpdated`, planned).

## Quality bar

- Each source has a clinical reviewer attribution.
- Out-of-date sources are flagged when their `effectiveFrom + 24 months` is past.
- Sources can be soft-disabled per tenant without deletion.

## Out of scope

- We do not redistribute copyrighted guideline text — only summaries with attribution and links.
- We do not host textbook content.
