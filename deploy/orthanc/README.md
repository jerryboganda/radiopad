# RadioPad — Bundled Orthanc DICOMweb Proxy

**Status:** Active  **Owner:** Integrations / PACS  **Last Updated:** 2026-05-04

This directory ships an opt-in [Orthanc](https://www.orthanc-server.com/) container that gives RadioPad a self-contained DICOMweb proxy when an enterprise's PACS is reachable only via classic DIMSE (C-STORE / C-FIND) or via a vendor portal that does not expose DICOMweb directly.

## When to use this

| Scenario | Use direct DICOMweb | Use bundled Orthanc |
| --- | :---: | :---: |
| Tenant PACS already exposes WADO-RS / QIDO-RS / STOW-RS | ✅ | |
| Tenant PACS speaks only classic DIMSE (DCMTK, dcm4chee 2.x) | | ✅ (Orthanc DIMSE ↔ DICOMweb bridge) |
| Tenant uses a vendor PACS without an exposable DICOMweb endpoint | | ✅ |
| HL7 ↔ DICOM bridging (ORM/ORU correlated to a study) | | ✅ (Orthanc Lua hooks) |

## Files

- [`Dockerfile.orthanc`](./Dockerfile.orthanc) — extends `jodogne/orthanc-plugins:latest` and bakes in the DICOMweb plugin + Lua hook directory.
- [`orthanc.json`](./orthanc.json) — config bound to `127.0.0.1:8042` by default with the DICOMweb plugin mounted at `/dicom-web/`.
- [`lua/`](./lua) — auto-loaded Lua scripts (HL7 ↔ DICOM bridging hooks).

## Wiring

The bundled Orthanc is enabled via the `pacs` Docker Compose profile:

```bash
docker compose -f deploy/docker-compose.yml --profile pacs up -d orthanc
# Orthanc UI:        http://127.0.0.1:8042
# DICOMweb endpoint: http://127.0.0.1:8042/dicom-web
```

Then point the tenant at this endpoint from `/admin/pacs`:

```
DICOMweb base URL: http://127.0.0.1:8042/dicom-web
```

The backend reports availability via `GET /api/pacs/health` when `RADIOPAD_ORTHANC_URL` is set in the API's environment.

## Security

- Orthanc binds `127.0.0.1` by default (matches the rest of the RadioPad stack — see [security-architecture.md](../../docs/04-security/security-architecture.md)).
- Default credentials are **disabled**: the `orthanc.json` template requires the operator to set `RADIOPAD_ORTHANC_USERNAME` / `RADIOPAD_ORTHANC_PASSWORD` env vars.
- No PHI is ever logged. Orthanc's verbose logging is disabled in the bundled config.
- Remote exposure requires an explicit operator decision (TLS reverse proxy + firewall rules); Compose does not expose the port to `0.0.0.0`.

## HL7 ↔ DICOM bridging

Orthanc's Lua hook layer is the supported way to correlate an HL7 ORM `Order` to an incoming DICOM C-STORE — write the accession number into the study's metadata, then call back into RadioPad's `/api/ingest/order` endpoint. See [`lua/orm-bridge.lua`](./lua/orm-bridge.lua) for the starter template.
