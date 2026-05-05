# PACS Bridge — operator runbook

**Status:** Active  **Owner:** Integrations / PACS  **Last Updated:** 2026-05-04

RadioPad ships three layers of PACS connectivity. Pick the one that matches the radiologist's hospital infrastructure.

## Decision tree

```
Does the hospital PACS expose DICOMweb (QIDO-RS / WADO-RS / STOW-RS)?
├── YES → Configure DICOMweb base URL on /admin/pacs (no extra container).
└── NO  → Does it speak DIMSE (C-STORE/C-FIND/C-MOVE)?
          ├── YES → Run the bundled Orthanc proxy (deploy/orthanc/) and point Orthanc at the DIMSE peer.
          └── NO  → Install a vendor signed plugin (Sectra / AGFA / Visage / Merge / Hyland)
                    via the desktop plugin SDK (desktop/plugin-sdk/).
```

## 1. Direct DICOMweb (recommended)

Set the tenant's DICOMweb base URL from `/admin/pacs`. Endpoints:

| API | Description |
| --- | --- |
| `GET /api/pacs/studies?accession=...` | QIDO-RS proxy. Vendor-neutral DICOM JSON Model. |
| `GET /api/reports/{id}/dicom-context` | Study-level QIDO using the report's accession number (PRD DCM-001..006). |
| `GET /api/reports/{id}/dicom-context/instance` | WADO-RS instance metadata (PRD DCM-007). |
| `POST /api/pacs/studies` | STOW-RS store proxy. |
| `GET /api/pacs/health` | Readiness probe (DICOMweb + bundled-Orthanc reachability). |

Audit: every QIDO/WADO/STOW call is logged as `AuditAction.DicomContextFetched`. **Accession numbers are never written to the audit row** — only a 12-hex prefix of `sha256(accession)`.

## 2. Bundled Orthanc proxy

Use the bundled Orthanc when the hospital PACS speaks classic DIMSE only, or when HL7 ↔ DICOM bridging is required.

```bash
# Start the proxy (binds 127.0.0.1:8042 by default).
docker compose -f deploy/docker-compose.yml --profile pacs up -d orthanc

# Configure RadioPad to use it.
export RADIOPAD_ORTHANC_URL=http://127.0.0.1:8042
# In the tenant settings UI, set DICOMweb base URL to:
#   http://127.0.0.1:8042/dicom-web
```

HL7 ↔ DICOM correlation is implemented in the Orthanc Lua hooks ([`deploy/orthanc/lua/orm-bridge.lua`](../../deploy/orthanc/lua/orm-bridge.lua)). The hook fires on `OnStableStudy` and POSTs to RadioPad's `/api/ingest/order`. See [deploy/orthanc/README.md](../../deploy/orthanc/README.md).

## 3. Signed vendor plugins (Sectra, AGFA, Visage, Merge, Hyland)

When neither DICOMweb nor DIMSE is available, RadioPad ships a signed-plugin SDK so a vendor can publish a workstation-side adapter that the radiologist installs locally. Specifications and example manifest are in [`desktop/plugin-sdk/`](../../desktop/plugin-sdk/).

Trust model: every plugin manifest is verified by SHA-256 + Ed25519 detached signature against `RADIOPAD_PLUGIN_PUBKEY` before load. See [desktop/PLUGIN_TRUST.md](../../desktop/PLUGIN_TRUST.md).

CLI:

```powershell
radiopad pacs plugins list
radiopad pacs plugins verify .\sectra-plugin.manifest.json
radiopad pacs plugins enable  sectra-pacs
radiopad pacs plugins disable sectra-pacs
```

## Locks

- Backend / Orthanc bind `127.0.0.1` by default. Remote exposure is an explicit operator decision.
- Audit log is append-only; PACS proxy writes through `IAuditLog.AppendAsync` only.
- Plugin signature verification reuses the iter-30 SHA-256 + Ed25519 verifier — no plugin loads without a valid signature in release builds.
- PHI minimisation in the audit row: ids + accession-hash + upstream status + byte counts only.
