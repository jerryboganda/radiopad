# RAG Architecture

**Status:** Planned (Phase 2)  ·  **Owner:** AI  ·  **Last Updated:** 2026-05-04

## Goal

Ground AI drafts in tenant-approved knowledge: rulebook narrative, internal protocols, current guideline summaries.

## Sources

| Source | Sensitivity | Refresh cadence |
| --- | --- | --- |
| Rulebook descriptions | Confidential (tenant) | On rulebook save. |
| Internal protocol PDFs (uploaded by admin) | Confidential | Manual upload. |
| Public radiology guidelines (curated) | Public | Quarterly. |
| Tenant glossary | Confidential | On admin save. |

PHI is **never** indexed.

## Pipeline

```
Source → text extractor → splitter → embedder → vector store → retriever → AiGateway
```

- **Extractor:** PDF → text via `PdfPig`-equivalent; HTML → text via Readability.
- **Splitter:** semantic chunks of 500–1000 tokens with overlap.
- **Embedder:** provider-supplied embeddings model. Compliance class drives provider choice.
- **Vector store:** Postgres `pgvector` (preferred to keep one DB). Per-tenant index.
- **Retriever:** top-k cosine similarity with tenant filter.

## Tenancy

- Vector store rows carry `TenantId`.
- Queries always include the tenant filter.
- Cross-tenant retrieval is forbidden.

## Compliance & PHI

- Indexed sources do not contain PHI.
- The query may contain PHI (a section of findings), so the embedding call obeys the same provider compliance routing as generation.
- The retrieved chunks are appended to the prompt as additional context; they go to the same compliant provider.

## Update cadence

- Rulebook saves trigger re-embedding of the touched rulebook.
- Protocol PDFs re-embed on admin upload.
- Public guideline corpus refreshes via a CLI command.

## Failure modes

- Embedding service down → fall back to vanilla prompt (without RAG); audit `RagDegraded` (Phase 2).
- Vector index drift → CLI `radiopad rag rebuild --tenant <slug>`.

## Cost

- Indexing: one-time per source; cached.
- Query: 1 embedding call per request + 1 vector search; the generation call is unchanged.
