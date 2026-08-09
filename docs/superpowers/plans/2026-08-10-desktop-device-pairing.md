# Desktop Device Pairing Hub & Global Companion Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Provide a dedicated "Device pairing" option in the desktop app's left navigation menu where users can pair their mobile companion, test speech-to-text and voice commands in an interactive sandbox, and maintain a persistent pairing session across all reporting workflows.

**Architecture:** App-level React Context (`CompanionProvider`) mounted in `AppShell.tsx` manages the companion session, WebSocket relay, WebRTC direct LAN connection, and on-device transcription dispatching. The `/device-pairing` route hosts the pairing controller, diagnostics, and a pre-reporting test sandbox that plugs into `sectionEditorRegistry`. `ReportClient` and `ComposerRibbon` seamlessly connect to the global companion context.

**Tech Stack:** Next.js 16 (App Router), React 18, TypeScript, next-intl, lucide-react, qrcode, WebRTC, Vitest, Testing Library.

## Global Constraints
- Dedicated route: `/device-pairing`
- Navigation group: `workspace` in `frontend/components/shell/nav.config.tsx`
- Surface target: `desktop`
- Permission: visible to all signed-in users on desktop
- Strict type safety, clean React hooks, no any casts or loose types

---

### Task 1: Navigation & Internationalization

**Files:**
- Modify: `frontend/components/shell/nav.config.tsx`
- Modify: `frontend/messages/en.json`
- Modify: `frontend/messages/de.json`
- Modify: `frontend/messages/es.json`
- Modify: `frontend/messages/fr.json`
- Modify: `frontend/messages/hi.json`
- Modify: `frontend/messages/pt.json`
- Test: `frontend/__tests__/navDevicePairing.test.ts`

**Interfaces:**
- Produces: `navGroups` with `/device-pairing` under `workspace` group (`labelKey: 'devicePairing'`).

- [ ] **Step 1: Write the failing unit test for navigation configuration**

Create `frontend/__tests__/navDevicePairing.test.ts`:
```typescript
import { describe, it, expect } from 'vitest';
import { navGroups, Icons } from '../components/shell/nav.config';
import enMessages from '../messages/en.json';

describe('Navigation — Device Pairing item', () => {
  it('includes /device-pairing in the workspace group for desktop surface', () => {
    const workspaceGroup = navGroups.find((g) => g.labelKey === 'workspace');
    expect(workspaceGroup).toBeDefined();
    const item = workspaceGroup?.items.find((it) => it.href === '/device-pairing');
    expect(item).toBeDefined();
    expect(item?.labelKey).toBe('devicePairing');
    expect(item?.icon).toBe(Icons.devicePairing);
    expect(item?.permission).toBeUndefined(); // visible to all signed-in users
  });

  it('has translation for devicePairing in en.json', () => {
    expect((enMessages.nav as Record<string, string>).devicePairing).toBe('Device pairing');
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm --filter @radiopad/frontend test frontend/__tests__/navDevicePairing.test.ts`  
Expected: FAIL (missing `devicePairing` in `navGroups` and `Icons`).

- [ ] **Step 3: Update `nav.config.tsx` and i18n message bundles**

In `frontend/components/shell/nav.config.tsx`:
- Import `Smartphone` (or `TabletSmartphone`) from `lucide-react`.
- Add `devicePairing: Smartphone` to `Icons`.
- Add `{ href: '/device-pairing', labelKey: 'devicePairing', icon: Icons.devicePairing, surfaces: ['desktop'] }` to the `workspace` group (e.g. right beside `/reports/compose` or `/notifications`).

In `frontend/messages/en.json`, `de.json`, `es.json`, `fr.json`, `hi.json`, `pt.json`:
- Add `"devicePairing"` under `"nav"`:
  - `en.json`: `"devicePairing": "Device pairing"`
  - `de.json`: `"devicePairing": "Gerätekopplung"`
  - `es.json`: `"devicePairing": "Emparejamiento de dispositivos"`
  - `fr.json`: `"devicePairing": "Couplage d'appareils"`
  - `hi.json`: `"devicePairing": "डिवाइस पेयरिंग"`
  - `pt.json`: `"devicePairing": "Emparelhamento de dispositivos"`

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm --filter @radiopad/frontend test frontend/__tests__/navDevicePairing.test.ts`  
Expected: PASS.

- [ ] **Step 5: Commit changes**

```bash
git add frontend/components/shell/nav.config.tsx frontend/messages/*.json frontend/__tests__/navDevicePairing.test.ts
git commit -m "feat(desktop): add device pairing option to workspace navigation and locales"
```

---

### Task 2: Global `CompanionProvider` & Shell Integration

**Files:**
- Create: `frontend/components/companion/CompanionContext.tsx`
- Modify: `frontend/components/shell/AppShell.tsx`
- Test: `frontend/__tests__/companionContext.test.tsx`

**Interfaces:**
- Produces: `CompanionProvider`, `useCompanion()`, `CompanionContextType`
```typescript
export interface CompanionContextType {
  phase: 'idle' | 'advertising' | 'paired' | 'error';
  link: 'idle' | 'connecting' | 'connected' | 'failed';
  sessionId: string | null;
  pairingCode: string | null;
  pairingPayload: string | null;
  qrDataUrl: string | null;
  companionDeviceName: string | null;
  phoneListening: boolean;
  transcribing: boolean;
  slowTranscribe: boolean;
  error: string | null;
  lastCommand: string | null;
  lastTranscript: string | null;
  startPairing: () => Promise<void>;
  unpair: () => Promise<void>;
  retryRtc: () => void;
  clearError: () => void;
}
```

- [ ] **Step 1: Write the failing unit test for CompanionContext**

Create `frontend/__tests__/companionContext.test.tsx`:
```typescript
import { describe, it, expect, vi } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { CompanionProvider, useCompanion } from '../components/companion/CompanionContext';
import * as apiModule from '../lib/api';

describe('CompanionContext', () => {
  it('provides initial idle state', () => {
    const { result } = renderHook(() => useCompanion(), {
      wrapper: CompanionProvider,
    });
    expect(result.current.phase).toBe('idle');
    expect(result.current.link).toBe('idle');
    expect(result.current.sessionId).toBeNull();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm --filter @radiopad/frontend test frontend/__tests__/companionContext.test.tsx`  
Expected: FAIL (missing `CompanionContext.tsx`).

- [ ] **Step 3: Implement `CompanionContext.tsx` and wire into `AppShell.tsx`**

Create `frontend/components/companion/CompanionContext.tsx` consolidating session creation, QR generation, WebSocket messaging, WebRTC signaling & direct audio receiver, speech transcription with on-device timeout recovery, and editor dispatch.

Update `frontend/components/shell/AppShell.tsx` to wrap `{shell}` with `<CompanionProvider>` inside `AuthGate`.

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm --filter @radiopad/frontend test frontend/__tests__/companionContext.test.tsx`  
Expected: PASS.

- [ ] **Step 5: Commit changes**

```bash
git add frontend/components/companion/CompanionContext.tsx frontend/components/shell/AppShell.tsx frontend/__tests__/companionContext.test.tsx
git commit -m "feat(desktop): implement global CompanionProvider and mount in AppShell"
```

---

### Task 3: Pre-Reporting Live Dictation & Voice Command Test Sandbox

**Files:**
- Create: `frontend/components/companion/CompanionTestSandbox.tsx`
- Test: `frontend/__tests__/companionTestSandbox.test.tsx`

**Interfaces:**
- Consumes: `useCompanion()` from `CompanionContext.tsx`, `registerSectionEditor` from `@/lib/editor/sectionEditorRegistry`
- Produces: `<CompanionTestSandbox />` component with interactive practice Findings & Impression sections, live command activity feedback, and reset tools.

- [ ] **Step 1: Write failing unit test for `CompanionTestSandbox`**

Create `frontend/__tests__/companionTestSandbox.test.tsx`:
```typescript
import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import CompanionTestSandbox from '../components/companion/CompanionTestSandbox';
import { CompanionProvider } from '../components/companion/CompanionContext';

describe('CompanionTestSandbox', () => {
  it('renders Findings and Impression practice fields and command cheat sheet', () => {
    render(
      <CompanionProvider>
        <CompanionTestSandbox />
      </CompanionProvider>
    );
    expect(screen.getByText(/practice findings/i)).toBeInTheDocument();
    expect(screen.getByText(/practice impression/i)).toBeInTheDocument();
    expect(screen.getByText(/voice commands/i)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm --filter @radiopad/frontend test frontend/__tests__/companionTestSandbox.test.tsx`  
Expected: FAIL.

- [ ] **Step 3: Implement `CompanionTestSandbox.tsx`**

Implement `frontend/components/companion/CompanionTestSandbox.tsx`:
- Render two practice text areas (`findings`, `impression`) that hook into `registerSectionEditor` when mounted.
- Include live interim preview support (`setInterim`, `clearInterim`).
- Include voice command badges that highlight in real time when `lastCommand` changes (e.g. `next_section`, `jump_findings`, `jump_impression`, `undo`, `new_line`).
- Provide "Clear Practice Text" button.

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm --filter @radiopad/frontend test frontend/__tests__/companionTestSandbox.test.tsx`  
Expected: PASS.

- [ ] **Step 5: Commit changes**

```bash
git add frontend/components/companion/CompanionTestSandbox.tsx frontend/__tests__/companionTestSandbox.test.tsx
git commit -m "feat(desktop): implement pre-reporting dictation and voice command test sandbox"
```

---

### Task 4: Dedicated Device Pairing Page (`/device-pairing`)

**Files:**
- Create: `frontend/app/(desktop)/device-pairing/page.tsx`
- Test: `frontend/__tests__/devicePairingPage.test.tsx`

**Interfaces:**
- Consumes: `useCompanion()`, `CompanionTestSandbox`, `api.localModels.list()`
- Produces: Full Device Pairing management route at `/device-pairing`.

- [ ] **Step 1: Write failing unit test for `/device-pairing` page**

Create `frontend/__tests__/devicePairingPage.test.tsx`:
```typescript
import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import DevicePairingPage from '../app/(desktop)/device-pairing/page.tsx';
import { CompanionProvider } from '../components/companion/CompanionContext';

describe('DevicePairingPage', () => {
  it('renders the header, pairing controller, and test sandbox', () => {
    render(
      <CompanionProvider>
        <DevicePairingPage />
      </CompanionProvider>
    );
    expect(screen.getByRole('heading', { name: /device pairing/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /start pairing/i })).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm --filter @radiopad/frontend test frontend/__tests__/devicePairingPage.test.tsx`  
Expected: FAIL.

- [ ] **Step 3: Implement `frontend/app/(desktop)/device-pairing/page.tsx`**

Build the comprehensive page with:
- Page Header: Title, subtitle, surface badge.
- Pairing Controller Card:
  - Idle state with Start Pairing button and feature highlights.
  - Advertising state with QR Code, 6-character code, Copy Link button with clipboard copy toast, and Cancel button.
  - Paired state with Device Card, Connection Mode (Direct LAN WebRTC / Cloud Relay), Live mic status indicator, Speech engine readiness badge, and Unpair / Reconnect buttons.
  - Error state with alert banner and Retry button.
- Embedded `<CompanionTestSandbox />` for immediate audio verification.
- Quick navigation shortcuts: "Open Worklist", "Open Report Composer".

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm --filter @radiopad/frontend test frontend/__tests__/devicePairingPage.test.tsx`  
Expected: PASS.

- [ ] **Step 5: Commit changes**

```bash
git add frontend/app/\(desktop\)/device-pairing/page.tsx frontend/__tests__/devicePairingPage.test.tsx
git commit -m "feat(desktop): implement dedicated /device-pairing page"
```

---

### Task 5: Report Composer Integration & `CompanionHostPanel` Refactor

**Files:**
- Modify: `frontend/components/reports/ComposerRibbon.tsx`
- Modify: `frontend/app/(desktop)/reports/[id]/ReportClient.tsx`
- Modify: `frontend/components/companion/CompanionHostPanel.tsx`
- Test: `frontend/__tests__/composerCompanionIntegration.test.tsx`

**Interfaces:**
- Consumes: `useCompanion()` in `ComposerRibbon` and `ReportClient`.

- [ ] **Step 1: Write failing test for Composer Ribbon companion status**

Create `frontend/__tests__/composerCompanionIntegration.test.tsx`:
```typescript
import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import ComposerRibbon from '../components/reports/ComposerRibbon';
import { CompanionProvider } from '../components/companion/CompanionContext';

describe('ComposerRibbon with global companion', () => {
  it('renders companion button with paired indicator when paired', () => {
    // Tests ribbon rendering with companion context
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm --filter @radiopad/frontend test frontend/__tests__/composerCompanionIntegration.test.tsx`  
Expected: FAIL.

- [ ] **Step 3: Refactor `ComposerRibbon`, `ReportClient`, and `CompanionHostPanel`**

- Update `ComposerRibbon` to show companion status (connected device name, active listening dot) directly from `CompanionContext`.
- Update `CompanionHostPanel` to consume `useCompanion()` rather than maintaining a disconnected local session, ensuring a single shared connection across the app.
- Update `ReportClient` so opening any report uses the existing active pairing session.

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm --filter @radiopad/frontend test frontend/__tests__/composerCompanionIntegration.test.tsx`  
Expected: PASS.

- [ ] **Step 5: Commit changes**

```bash
git add frontend/components/reports/ComposerRibbon.tsx frontend/app/\(desktop\)/reports/\[id\]/ReportClient.tsx frontend/components/companion/CompanionHostPanel.tsx frontend/__tests__/composerCompanionIntegration.test.tsx
git commit -m "refactor(desktop): integrate report composer and ribbon with global companion context"
```

---

### Task 6: End-to-End Verification & Polish

**Files:**
- All modified & created files

- [ ] **Step 1: Run typechecks and linter across the entire project**
```bash
pnpm --filter @radiopad/frontend typecheck
pnpm --filter @radiopad/frontend lint
```

- [ ] **Step 2: Run all frontend unit tests**
```bash
pnpm --filter @radiopad/frontend test
```

- [ ] **Step 3: Run surface builds to ensure static bundling succeeds**
```bash
pnpm --filter @radiopad/frontend run build:desktop
```

- [ ] **Step 4: Commit final verification artifact and update walkthrough**
