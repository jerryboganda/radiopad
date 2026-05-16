-- RadioPad — Orthanc Lua bridge (SR-stored hook).
--
-- Iter-33 INT-008. Fires on `OnStoredInstance` and forwards the DICOM JSON
-- tag payload of any Modality=SR instance to:
--   POST $RADIOPAD_BRIDGE_URL/api/integrations/orthanc/sr-stored
--   Authorization: Bearer $RADIOPAD_BRIDGE_TOKEN
--   Content-Type:  application/json
--
-- The body is the verbatim Orthanc DICOM-tags JSON for the instance, which
-- the RadioPad backend feeds into DicomSrToHl7Converter to produce an
-- ORU^R01 enqueued on the in-process HL7 outbox.

local function bridge_base()
  return os.getenv('RADIOPAD_BRIDGE_URL') or 'http://radiopad-api:7457'
end

local function bridge_token()
  return os.getenv('RADIOPAD_BRIDGE_TOKEN') or ''
end

function OnStoredInstance(instanceId, tags, metadata)
  local modality = tags['Modality'] or ''
  if modality ~= 'SR' then
    return
  end
  local token = bridge_token()
  if token == '' then
    print('radiopad-sr-store: RADIOPAD_BRIDGE_TOKEN unset; skipping POST.')
    return
  end

  -- Pull the full DICOM-as-JSON payload (DCM4CHE-style tag dictionary) for
  -- the stored instance. Orthanc returns this as a JSON string when called
  -- with `?simplify=false&short=false`.
  local payload, err = RestApiGet('/instances/' .. instanceId .. '/tags')
  if payload == nil then
    print('radiopad-sr-store: RestApiGet failed: ' .. tostring(err))
    return
  end

  local url = bridge_base() .. '/api/integrations/orthanc/sr-stored'
  local ok, postErr = pcall(function()
    HttpPost(url, payload, {
      ['Authorization'] = 'Bearer ' .. token,
      ['Content-Type']  = 'application/json',
    })
  end)
  if not ok then
    print('radiopad-sr-store: sr-stored POST failed: ' .. tostring(postErr))
  else
    print('radiopad-sr-store: forwarded SR ' .. instanceId)
  end
end
