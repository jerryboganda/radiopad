**Status:** Active  **Owner:** Integrations  **Last Updated:** 2026-05-04

# Orthanc bridge (HL7 v2 ↔ DICOM SR)

Iter-33 / INT-008. Replaces the iter-30 stub that only logged study ids with
a real bidirectional bridge between Orthanc (the open-source DICOM/PACS) and
the RadioPad backend. Two Orthanc Lua hooks call the RadioPad API; the
backend converts inbound DICOM SR JSON to an HL7 v2.5 ORU^R01 message that
the existing RIS pipeline can consume.

## Message flow

```
   ┌──────────────┐  OnStableStudy   ┌─────────────────────────────┐
   │   Orthanc    │ ───────────────► │ POST /study-stable          │ → audit StudyReceived
   │   (PACS)     │                  │  Bearer RADIOPAD_BRIDGE_TOKEN│
   │              │  OnStoredInstance│                              │
   │              │ ───────────────► │ POST /sr-stored             │ → DicomSrToHl7Converter
   └──────────────┘   (Modality=SR)   │                              │   → IHl7Outbox.Enqueue
                                       │                              │   → audit OrderIngested
                                       └─────────────────────────────┘
```

* `radiopad-bridge.lua` — fires `OnStableStudy`. Serializes a minimal study
  summary (PatientID, AccessionNumber, StudyInstanceUID, Modality,
  StudyDate) and POSTs it to `/api/integrations/orthanc/study-stable`.
* `radiopad-sr-store.lua` — fires `OnStoredInstance` for any `Modality=SR`
  instance, fetches the full DICOM-tags JSON from Orthanc's REST API, and
  POSTs it to `/api/integrations/orthanc/sr-stored`.

## Tag map (HL7 v2 ORU^R01 ↔ DICOM SR)

| HL7 field                                     | DICOM SR tag           | VR  | Notes                                         |
| --------------------------------------------- | ---------------------- | --- | --------------------------------------------- |
| MSH-10 (Message Control ID)                   | (regenerated)          | —   | Fresh GUID-derived on every conversion.       |
| OBR-3 (Filler Order / Accession)              | `00080050`             | SH  | Round-trips both directions.                  |
| PID-3 (Patient ID, opaque reference)          | `00100020`             | LO  | No PHI beyond the opaque ID.                  |
| —                                             | `00080016` SOP Class   | UI  | Always Basic Text SR `1.2.840.10008.5.1.4.1.1.88.11`. |
| —                                             | `00080018` SOP Inst    | UI  | Fresh `2.25.<guid-as-int>` on HL7 → SR.       |
| OBX-2 (Value Type, fixed `TX`)                | content `0040A040` `TEXT` | CS | Only TEXT items map back to OBX.              |
| OBX-3 (Observation Identifier)                | content `0040A043` Concept Name | SQ  | OBX-3 component-1 ↔ `00080100` Code Value.    |
| OBX-5 (Observation Value)                     | content `0040A160`     | UT  | Round-trip target asserted by tests.          |
| —                                             | `0040A040` (root)      | CS  | Always `CONTAINER`.                           |
| —                                             | `0040A730`             | SQ  | ContentSequence — one item per OBX TEXT.      |

## Endpoints

Both endpoints live on the standard backend port (`7457`) and are protected
by a single shared bearer; the bridge is the only caller, so token rotation
is a Lua container restart.

* `POST /api/integrations/orthanc/study-stable` — body: `StudyStableDto`.
  Audits `AuditAction.StudyReceived` for the configured tenant.
* `POST /api/integrations/orthanc/sr-stored` — body: DICOM SR JSON tag
  dictionary. Runs `DicomSrToHl7Converter`, enqueues the resulting
  ORU^R01 on `IHl7Outbox`, and audits `AuditAction.OrderIngested` with
  `source: orthanc-sr` so the support team can distinguish SR-derived
  messages from MLLP-delivered ones.

400 if the body is not a JSON object or `00080050` is missing.
401 if the bearer is absent / wrong (compared with
`CryptographicOperations.FixedTimeEquals`).
503 if `RADIOPAD_BRIDGE_TENANT` resolves to an unknown tenant slug.

## Environment variables

| Variable                  | Default                              | Required | Notes                                                                                  |
| ------------------------- | ------------------------------------ | -------- | -------------------------------------------------------------------------------------- |
| `RADIOPAD_BRIDGE_URL`     | `http://radiopad-api:7457`           | no       | Orthanc-side. Override when running against an external RadioPad host.                 |
| `RADIOPAD_BRIDGE_TOKEN`   | _(empty)_                            | **yes**  | Bearer compared in constant time. Empty token disables the bridge on both ends.        |
| `RADIOPAD_BRIDGE_TENANT`  | `dev`                                | no       | Tenant slug whose audit log + outbox receive the bridged events.                       |

## Security posture

* Backend bind stays at `127.0.0.1` by default. The Orthanc container reaches
  it over the docker-compose network; remote access still requires an
  explicit `RADIOPAD_BIND` opt-in plus a TLS reverse proxy.
* Bearer compared with `CryptographicOperations.FixedTimeEquals` — no
  early-out on mismatch length except length-difference, which is
  unavoidable and not a useful side-channel for high-entropy tokens.
* PHI minimisation: only AccessionNumber + opaque PatientID + StudyInstanceUID
  + Modality + StudyDate cross the bridge from `study-stable`. SR bodies are
  the radiologist's own narrative — no patient name/DOB/address is added by
  the bridge.
* Audit chain stays append-only: every accepted POST writes exactly one row
  through `IAuditLog.AppendAsync`, and the integrity-chain hash includes
  `DetailsJson` so tampering is detectable by `GET /api/audit/verify`.
* The HL7 outbox is in-memory (`InMemoryHl7Outbox`) for v0.1; a future
  iteration will swap in a database-backed implementation behind the same
  `IHl7Outbox` interface.

## Tests

* `RadioPad.Api.Tests/Iter33/Hl7DicomSrRoundTripTests.cs` — synthetic
  ORU^R01 → SR → ORU^R01 round-trip, asserts OBR-3 (`ACC1`) and OBX-5
  preservation, plus uniqueness of the generated SOPInstanceUID.
* `RadioPad.Api.Tests/Iter33/OrthancBridgeControllerTests.cs` — full HTTP
  round-trip; asserts 401 on a wrong bearer, audit emission for both
  endpoints, and outbox handoff for an SR carrying a single TEXT content
  item.
