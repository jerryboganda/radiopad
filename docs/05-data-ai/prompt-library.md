# Prompt Library

**Status:** Current (skeleton)  ·  **Owner:** AI  ·  **Last Updated:** 2026-05-04

> Operational prompt catalog. Each entry documents id, purpose, system message, user template, and the version policy.

## `report.impression.v1.0`

- **Purpose:** Draft an impression from findings + indication.
- **Inputs:** `modality`, `bodyPart`, `indication`, `comparison?`, `findings`.
- **Output:** `Impression: ...` (+ optional caveats).
- **System (extract):** "You are a radiology reporting assistant. Draft a concise impression. Use only what the findings support. Do not produce diagnoses unsupported by the findings. Never sign or finalize the report."

## `report.recommendation.v1.0`

- **Purpose:** Draft follow-up recommendations from findings + impression.
- **Inputs:** `findings`, `impression`.
- **Output:** `Recommendations: ...`.
- **System (extract):** "Draft follow-up recommendations consistent with the impression. Cite standard guidelines by name where appropriate. Do not invent specific values not supported by the findings."

## `report.technique.v1.0`

- **Purpose:** Boilerplate technique paragraph.
- **Inputs:** `modality`, `bodyPart`, optional contrast info.
- **Output:** plain paragraph.
- **System (extract):** "Produce a brief, accurate technique paragraph. Do not include patient identifiers."

## `validation.explain.v1.0` (Phase 2)

- **Purpose:** One-line plain-language explanation of a validation finding.
- **Inputs:** rulebook id + version, finding code, snippet.
- **Output:** one sentence.

## `audit.summarise.v1.0` (Phase 3)

- **Purpose:** Operator-facing summary of an audit-event window.
- **Inputs:** event list.
- **Output:** structured summary.

## Version policy

- MINOR: prompt wording tweaks that pass the eval suite.
- MAJOR: contract changes (input fields, output schema). Old version stays available for one minor release.

## Linking from code

```csharp
public static class Prompts
{
    public const string ReportImpressionV10 = "report.impression.v1.0";
    // …
}
```

Adapters log the prompt id with each request so the audit log can correlate.
