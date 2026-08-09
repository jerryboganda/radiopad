# Design Specification: Desktop Device Pairing Hub & Global Companion Integration

**Date**: 2026-08-10  
**Status**: Approved  
**Topic**: Desktop Left Nav Dedicated Option for Device Pairing & Pre-Reporting Test Sandbox  

---

## 1. Overview & Problem Statement

RadioPad supports mobile phone companion pairing where a mobile device acts as an ultra-low-latency wireless microphone and remote control for the desktop radiology reporting workstation. Previously, companion pairing was only accessible from inside an active report (`/reports/[id]`), requiring radiologists to open a specific report before they could connect their microphone. Furthermore, navigating between pages severed or recreated local connections.

This feature introduces:
1. A dedicated **Device Pairing** option for all users in the primary desktop left navigation (`/device-pairing` under the Workspace group).
2. An application-level **CompanionContext** to persist pairing sessions across the desktop shell.
3. A pre-reporting **Live Dictation & Voice Command Test Sandbox** on the Device Pairing page, allowing clinicians to test audio streaming, local speech engine transcription, and remote commands before opening patient studies.
4. Seamless integration with the report composer, ensuring any opened report immediately benefits from the active companion connection.

---

## 2. Information Architecture & Navigation

- **Sidebar Menu**:
  - Located under the primary `workspace` group in `frontend/components/shell/nav.config.tsx`.
  - Item details:
    - Route: `/device-pairing`
    - Label Key: `devicePairing`
    - Icon: `Smartphone` (`lucide-react`)
    - Surface Scope: `['desktop']`
    - Permissions: None required (visible to all signed-in users on desktop).
- **Internationalization**:
  - `en.json`: `"devicePairing": "Device pairing"`
  - `de.json`: `"devicePairing": "Gerätekopplung"`
  - `es.json`: `"devicePairing": "Emparejamiento de dispositivos"`
  - `fr.json`: `"devicePairing": "Couplage d'appareils"`
  - `hi.json`: `"devicePairing": "डिवाइस पेयरिंग"`
  - `pt.json`: `"devicePairing": "Emparelhamento de dispositivos"`

---

## 3. Architecture & Global State (`CompanionContext`)

### 3.1 Context Provider (`frontend/components/companion/CompanionContext.tsx`)

Mounted in the desktop layout (`frontend/app/(desktop)/layout.tsx`), managing:
- **Session Lifecycle**:
  - `phase`: `'idle' | 'advertising' | 'paired' | 'error'`
  - `link`: `'idle' | 'connecting' | 'connected' | 'failed'` (WebRTC LAN link)
  - `sessionId`, `pairingCode`, `pairingPayload`, `qrDataUrl`, `companionDeviceName`
- **Audio & Transcription Stream**:
  - `phoneListening`: Boolean indicating push-to-talk (PTT) status.
  - `transcribing`: Boolean indicating speech engine busy status.
  - `slowTranscribe`: Boolean indicating engine cold-start / model loading hint.
  - `error`: Error message string or null.
  - `lastCommand`: Most recent `CompanionCommand` received from the companion.
  - `lastTranscript`: Most recent committed transcript text.
- **Methods**:
  - `startPairing()`: Initiates session on `/api/companion/sessions`, generates QR code and pairing payload, connects WebSocket relay.
  - `unpair()`: Gracefully tears down WebRTC peer, closes WebSocket, calls `/api/companion/sessions/{id}` DELETE, resets state.
  - `retryRtc()`: Retries WebRTC LAN connection.
  - `insertDirectly(text: string)`: Inserts text into current target (active report section or test sandbox).
  - `dispatchCommand(command: CompanionCommand)`: Executes navigation and editing commands.

### 3.2 Target Resolution
- Dictation and commands route to `getLastFocusedSectionEditor()` if present (e.g. Findings or Impression in an open report or the Test Sandbox).
- If no editor is explicitly focused, falls back to the primary target (`findings` section in reports, or `sandbox-findings` on the pairing page).

---

## 4. UI & Features on `/device-pairing`

### 4.1 Pairing Controller & Diagnostics Card
- **Idle State**: Branded header, feature summary, and large **"Start pairing"** button.
- **Advertising State**:
  - High-resolution QR code for camera scanning.
  - Formatted 6-character monospace code for manual input.
  - "Copy pairing link" action with copied feedback toast.
  - Cancel pairing button.
- **Paired State**:
  - Connected device card with device name (e.g., *iPhone 15 Pro*, *Pixel 8*), connection mode (*Direct LAN WebRTC* vs *Cloud Relay*), and audio stream status.
  - Live animated mic/equalizer indicator reflecting phone PTT activation.
  - Speech engine readiness indicator (with quick link to Settings $\rightarrow$ On-device models if models are downloading or uninstalled).
  - Action buttons: "Unpair / Disconnect", "Retry LAN Link", and "Go to Worklist".

### 4.2 Pre-Reporting Test Sandbox
- **Practice Clinical Sections**:
  - Two simulated report sections: **Findings** and **Impression**.
  - Registered with `sectionEditorRegistry` so all companion commands (`next_section`, `prev_section`, `jump_findings`, `jump_impression`, `undo`, `new_line`, `generate_impression`) work identically in practice.
- **Live Command Activity Feed**:
  - Real-time pill badges showing executed voice commands with timestamp indicators.
- **Interim & Finalized Transcript Display**:
  - Ghost-text live interim preview as the radiologist speaks, committing clean formatted text on pause.
- **Reset Sandbox Action**:
  - 1-click button to clear practice text and reset test state.

---

## 5. Report Composer Integration

- `ReportClient.tsx` & `ComposerRibbon.tsx` consume `useCompanion()` from `CompanionContext`.
- If already paired, the Composer Ribbon displays a active green "Paired: [Device Name]" badge.
- Clicking the companion button in the ribbon displays a streamlined status popover with connection metrics and a link to `/device-pairing`.
- If not paired, clicking the ribbon button can start pairing immediately or open the pairing modal without disrupting the report draft.

---

## 6. Error Handling & Resilience

- **Network Changes & Sleep Recovery**: If the device sleeps or network drops, auto-reconnection is attempted.
- **LAN WebRTC Fallback**: Clear diagnostics when local firewall or Wi-Fi client isolation prevents P2P audio streaming.
- **Cold Model Warning**: When on-device Whisper / MedASR models are cold-loading, visual hints reassure the user that audio is buffered and queued safely.

---

## 7. Verification Plan

1. **Unit & Component Testing**:
   - Verify `nav.config.tsx` includes `/device-pairing` under `workspace` for desktop surface.
   - Verify `CompanionContext` provides state and handles pairing/unpairing lifecycle.
   - Verify `/device-pairing` page renders idle, advertising, paired, and error states.
   - Verify Sandbox practice sections integrate with `sectionEditorRegistry`.
2. **Build & Typecheck**:
   - `pnpm --filter @radiopad/frontend run typecheck`
   - `pnpm --filter @radiopad/frontend run build`
3. **End-to-End Verification**:
   - Navigate from Sidebar $\rightarrow$ "Device pairing".
   - Start pairing, test QR display and manual code.
   - Simulate companion join and audio/dictation into the Sandbox.
   - Navigate from "Device pairing" $\rightarrow$ Worklist $\rightarrow$ Composer and verify companion remains paired.
