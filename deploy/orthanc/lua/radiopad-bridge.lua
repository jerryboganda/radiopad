-- RadioPad — Orthanc Lua bridge (study-stable hook).
--
-- Iter-33 INT-008. Fires on `OnStableStudy` and POSTs a minimal study
-- summary to the bearer-protected RadioPad endpoint:
--   POST $RADIOPAD_BRIDGE_URL/api/integrations/orthanc/study-stable
--   Authorization: Bearer $RADIOPAD_BRIDGE_TOKEN
--
-- Configuration (env vars, sourced from `docker-compose.yml`):
--   RADIOPAD_BRIDGE_URL    default http://radiopad-api:7457
--   RADIOPAD_BRIDGE_TOKEN  required (no default — bridge is no-op when missing)
--
-- PHI minimisation: only AccessionNumber, StudyInstanceUID, Modality and
-- StudyDate are forwarded plus an opaque PatientID reference. No patient
-- name, DOB or address ever leaves Orthanc through this hook.

local function bridge_base()
  return os.getenv('RADIOPAD_BRIDGE_URL') or 'http://radiopad-api:7457'
end

local function bridge_token()
  return os.getenv('RADIOPAD_BRIDGE_TOKEN') or ''
end

local function json_escape(s)
  if s == nil then return '' end
  s = tostring(s)
  s = s:gsub('\\', '\\\\')
  s = s:gsub('"', '\\"')
  s = s:gsub('\n', '\\n')
  s = s:gsub('\r', '\\r')
  s = s:gsub('\t', '\\t')
  return s
end

function OnStableStudy(studyId, tags, metadata)
  local token = bridge_token()
  if token == '' then
    print('radiopad-bridge: RADIOPAD_BRIDGE_TOKEN unset; skipping POST.')
    return
  end

  local body = string.format(
    '{"patientId":"%s","accessionNumber":"%s","studyInstanceUid":"%s","modality":"%s","studyDate":"%s"}',
    json_escape(tags['PatientID'] or ''),
    json_escape(tags['AccessionNumber'] or ''),
    json_escape(tags['StudyInstanceUID'] or ''),
    json_escape(tags['Modality'] or ''),
    json_escape(tags['StudyDate'] or ''))

  local url = bridge_base() .. '/api/integrations/orthanc/study-stable'
  local ok, err = pcall(function()
    HttpPost(url, body, {
      ['Authorization'] = 'Bearer ' .. token,
      ['Content-Type']  = 'application/json',
    })
  end)
  if not ok then
    print('radiopad-bridge: study-stable POST failed: ' .. tostring(err))
  else
    print('radiopad-bridge: study-stable POST ok for ' .. studyId)
  end
end
