# Search

**Status:** Current (basic)  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

## Today

The dashboard `/api/reports` endpoint supports a substring search query parameter `q`. Server-side it uses `EF.Functions.Like` over `AccessionNumber`, `BodyPart`, and `Indication`:

```csharp
if (!string.IsNullOrWhiteSpace(q))
{
    var p = $"%{q}%";
    query = query.Where(r =>
        EF.Functions.Like(r.AccessionNumber, p) ||
        EF.Functions.Like(r.BodyPart, p) ||
        EF.Functions.Like(r.Indication, p));
}
```

Pagination uses `skip / take` with a `take` clamp of `1..500`, and the response includes `X-Total-Count`.

## Limits

- No full-text indexing of `Findings` / `Impression` content (intentionally — those are large and PHI-laden).
- `LIKE %q%` is non-sargable; acceptable up to ~100k rows per tenant. Beyond that we will introduce a Postgres `tsvector` column or a dedicated index.

## Planned (Phase 2)

- **Postgres full-text search** over selected columns (NOT the report body) using a generated `tsvector` column.
- Filters become composable: `modality:CT body:chest status:Validated q:"left lower lobe"`.
- Tenant scope enforced at query time.
- A search audit event (`SearchPerformed`) records the query hash but **never** the raw query, to avoid logging PHI.

## Out of scope

- Cross-tenant search.
- Search inside attachments.
- Image search / DICOM tag search — these are PACS responsibilities.
