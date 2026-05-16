-- RadioPad — Orthanc Lua bridge stub.
--
-- Hooks fire when a DICOM instance lands in Orthanc. The operator is
-- expected to fill `notify_radiopad_of_study(...)` with a POST to RadioPad's
-- /api/ingest/order endpoint (or HL7 MLLP listener). Out-of-the-box this
-- script only logs the study id; no PHI leaves the container.

function notify_radiopad_of_study(studyId, accessionNumber)
  -- Fill in to notify RadioPad. Example (commented):
  -- local body = '{"accession":"' .. (accessionNumber or '') .. '","source":"orthanc"}'
  -- HttpPost('http://radiopad-api:7457/api/ingest/order', body, {
  --   ['Authorization'] = 'Bearer ' .. (os.getenv('RADIOPAD_INGEST_TOKEN') or ''),
  --   ['Content-Type']  = 'application/json',
  --   ['X-RadioPad-Tenant'] = (os.getenv('RADIOPAD_TENANT') or 'dev'),
  -- })
end

function OnStableStudy(studyId, tags, metadata)
  local accession = tags['AccessionNumber'] or ''
  -- PHI-minimised log — accession is not PHI but treat anyway.
  print('orthanc-bridge: stable study ' .. studyId .. ' acc=' .. (accession ~= '' and 'present' or 'missing'))
  notify_radiopad_of_study(studyId, accession)
end
