# Mobile Audio Dictation & Positive Findings Reporting Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a mobile-to-desktop audio dictation and positive findings reporting pipeline in RadioPad with dual AI transcription engines (local `medASR + 6-gram` 4.4% WER default, and cloud `UBAG Gemini/ChatGPT` configurable via Admin).

**Architecture:** 
- Mobile App (`frontend/app/(mobile)/reporting`): Reporting list, New Report form modal (Radiology ID, Name, Age, Gender, timestamp), and Audio Recorder with Pause/Resume/Stop/Submit.
- Backend API (`backend/RadioPad.Api`): C# .NET domain models (`Report`, `DictationAudio`), REST endpoints, async transcription background job, and SignalR real-time event broadcasting (`ReportingHub`).
- Transcription Engines (`backend/RadioPad.Api/src/RadioPad.Infrastructure`): Local `medASR` engine adapter + `UBAG` provider adapter (`UbagProviderAdapter`) with radiology prompt context.
- Desktop App (`frontend/app/(desktop)/reports/[id]`): "Positive Findings" tab with audio player controls, editable AI transcript box, and "Append to Main Findings" action button.
- Admin Panel (`frontend/app/(desktop)/admin/dictation-settings`): Engine switcher, UBAG model selector, radiology system prompt editor, and test transcription tool.

**Tech Stack:** Next.js (React 19, TypeScript, TailwindCSS), Capacitor 6, C# .NET 8 (EF Core, SignalR, xUnit), Tauri Desktop.

## Global Constraints

- **Language & Frameworks**: C# .NET 8 for Backend, React 19 / Next.js App Router for Frontend, Capacitor 6 for Mobile.
- **Default Transcription Engine**: Local `medASR + 6-gram model` (4.4% WER) must be default, fallback/switchable to `UBAG`.
- **Medical Context**: All cloud AI prompts must explicitly enforce radiology domain context.
- **Real-Time Sync**: Audio dictations submitted on mobile must broadcast via SignalR to connected Desktop clients.

---

### Task 1: Backend Domain Entities, DTOs & Persistence

**Files:**
- Create: `backend/RadioPad.Api/src/RadioPad.Domain/Entities/Report.cs`
- Create: `backend/RadioPad.Api/src/RadioPad.Domain/Entities/DictationAudio.cs`
- Create: `backend/RadioPad.Api/src/RadioPad.Application/Reporting/Dtos/ReportDtos.cs`
- Modify: `backend/RadioPad.Api/src/RadioPad.Infrastructure/Persistence/RadioPadDbContext.cs`
- Test: `backend/RadioPad.Api/tests/RadioPad.Domain.Tests/ReportingDomainTests.cs`

**Interfaces:**
- Consumes: None
- Produces: `Report`, `DictationAudio` entities, `CreateReportDto`, `DictationAudioDto`, EF Core `DbSet<Report>`, `DbSet<DictationAudio>`

- [ ] **Step 1: Write domain entity unit tests**

Create `backend/RadioPad.Api/tests/RadioPad.Domain.Tests/ReportingDomainTests.cs`:
```csharp
using RadioPad.Domain.Entities;
using Xunit;

namespace RadioPad.Domain.Tests;

public class ReportingDomainTests
{
    [Fact]
    public void CreateReport_InitializesWithDefaultValues()
    {
        var report = new Report
        {
            RadiologyId = "RAD-2026-001",
            PatientName = "Jane Doe",
            PatientAge = 52,
            PatientGender = "Female"
        };

        Assert.NotEqual(Guid.Empty, report.Id);
        Assert.Equal("RAD-2026-001", report.RadiologyId);
        Assert.Empty(report.Dictations);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/RadioPad.Api/tests/RadioPad.Domain.Tests/RadioPad.Domain.Tests.csproj`
Expected: Compilation failure due to missing `Report` class.

- [ ] **Step 3: Implement domain entities and DTOs**

Create `backend/RadioPad.Api/src/RadioPad.Domain/Entities/Report.cs`:
```csharp
namespace RadioPad.Domain.Entities;

public class Report
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string RadiologyId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public int PatientAge { get; set; }
    public string PatientGender { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Status { get; set; } = "Draft";
    public ICollection<DictationAudio> Dictations { get; set; } = new List<DictationAudio>();
}
```

Create `backend/RadioPad.Api/src/RadioPad.Domain/Entities/DictationAudio.cs`:
```csharp
namespace RadioPad.Domain.Entities;

public class DictationAudio
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReportId { get; set; }
    public Report? Report { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
    public string TranscriptionEngine { get; set; } = "medASR-6gram";
    public string Status { get; set; } = "Pending";
    public string? TranscribedText { get; set; }
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? TranscribedAt { get; set; }
    public string? ErrorMessage { get; set; }
}
```

Create `backend/RadioPad.Api/src/RadioPad.Application/Reporting/Dtos/ReportDtos.cs`:
```csharp
namespace RadioPad.Application.Reporting.Dtos;

public record CreateReportRequestDto(
    string RadiologyId,
    string PatientName,
    int PatientAge,
    string PatientGender
);

public record DictationAudioDto(
    Guid Id,
    Guid ReportId,
    string StoragePath,
    double DurationSeconds,
    string TranscriptionEngine,
    string Status,
    string? TranscribedText,
    DateTimeOffset UploadedAt,
    DateTimeOffset? TranscribedAt
);

public record ReportDto(
    Guid Id,
    string RadiologyId,
    string PatientName,
    int PatientAge,
    string PatientGender,
    DateTimeOffset CreatedAt,
    string Status,
    List<DictationAudioDto> Dictations
);
```

- [ ] **Step 4: Update DbContext**

Register `DbSet<Report>` and `DbSet<DictationAudio>` in `RadioPadDbContext.cs`.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test backend/RadioPad.Api/tests/RadioPad.Domain.Tests/RadioPad.Domain.Tests.csproj`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add backend/RadioPad.Api/src/RadioPad.Domain backend/RadioPad.Api/src/RadioPad.Application backend/RadioPad.Api/src/RadioPad.Infrastructure/Persistence backend/RadioPad.Api/tests/RadioPad.Domain.Tests
git commit -m "feat(backend): add Report and DictationAudio domain entities and DTOs"
```

---

### Task 2: Backend REST Endpoints & Real-Time SignalR Hub

**Files:**
- Create: `backend/RadioPad.Api/src/RadioPad.Api/Controllers/ReportingController.cs`
- Create: `backend/RadioPad.Api/src/RadioPad.Api/Hubs/ReportingHub.cs`
- Create: `backend/RadioPad.Api/src/RadioPad.Application/Reporting/Services/IReportingService.cs`
- Create: `backend/RadioPad.Api/src/RadioPad.Application/Reporting/Services/ReportingService.cs`
- Test: `backend/RadioPad.Api/tests/RadioPad.Api.Tests/ReportingControllerTests.cs`

**Interfaces:**
- Consumes: `Report`, `DictationAudio`, `ReportDto`
- Produces: `POST /api/v1/reporting/reports`, `GET /api/v1/reporting/reports`, `POST /api/v1/reporting/reports/{id}/dictations`, SignalR `ReportingHub`

- [ ] **Step 1: Write integration tests for ReportingController**

Create `backend/RadioPad.Api/tests/RadioPad.Api.Tests/ReportingControllerTests.cs` testing report creation and audio dictation upload endpoints.

- [ ] **Step 2: Implement ReportingService and SignalR Hub**

Create `IReportingService.cs` and `ReportingService.cs` handling CRUD for reports and saving audio files.
Create `ReportingHub.cs`:
```csharp
using Microsoft.AspNetCore.SignalR;

namespace RadioPad.Api.Hubs;

public class ReportingHub : Hub
{
    public async Task JoinReportGroup(string reportId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"report-{reportId}");
    }

    public async Task LeaveReportGroup(string reportId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"report-{reportId}");
    }
}
```

- [ ] **Step 3: Implement ReportingController**

Create `ReportingController.cs` exposing REST endpoints.

- [ ] **Step 4: Run controller unit tests**

Run: `dotnet test backend/RadioPad.Api/tests/RadioPad.Api.Tests/RadioPad.Api.Tests.csproj`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add backend/RadioPad.Api/src/RadioPad.Api/Controllers backend/RadioPad.Api/src/RadioPad.Api/Hubs backend/RadioPad.Api/src/RadioPad.Application/Reporting
git commit -m "feat(backend): add ReportingController REST endpoints and SignalR ReportingHub"
```

---

### Task 3: Dual Transcription Engine Pipeline (`medASR` Local & `UBAG` Cloud)

**Files:**
- Create: `backend/RadioPad.Api/src/RadioPad.Application/Transcription/ITranscriptionEngine.cs`
- Create: `backend/RadioPad.Api/src/RadioPad.Infrastructure/Transcription/MedAsrTranscriptionEngine.cs`
- Create: `backend/RadioPad.Api/src/RadioPad.Infrastructure/Transcription/UbagTranscriptionEngine.cs`
- Create: `backend/RadioPad.Api/src/RadioPad.Infrastructure/Transcription/TranscriptionOrchestrator.cs`
- Test: `backend/RadioPad.Api/tests/RadioPad.Infrastructure.Tests/TranscriptionEngineTests.cs`

**Interfaces:**
- Consumes: Audio Stream, `IUbagClient`
- Produces: `ITranscriptionEngine`, `TranscriptionOrchestrator.TranscribeAsync(...)`

- [ ] **Step 1: Write engine unit tests**

Create tests verifying `MedAsrTranscriptionEngine` default fallback and `UbagTranscriptionEngine` radiology prompt construction.

- [ ] **Step 2: Implement MedAsrTranscriptionEngine and UbagTranscriptionEngine**

Implement local `medASR + 6-gram` driver (WER 4.4% default) and UBAG Gemini/ChatGPT adapter using `UbagProviderAdapter`.

- [ ] **Step 3: Implement TranscriptionOrchestrator**

Check Admin settings and route transcription request to active engine.

- [ ] **Step 4: Run engine tests**

Run: `dotnet test backend/RadioPad.Api/tests/RadioPad.Infrastructure.Tests/RadioPad.Infrastructure.Tests.csproj`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add backend/RadioPad.Api/src/RadioPad.Application/Transcription backend/RadioPad.Api/src/RadioPad.Infrastructure/Transcription
git commit -m "feat(transcription): add medASR local (4.4% WER) and UBAG cloud transcription dual-engine pipeline"
```

---

### Task 4: Mobile App Reporting Navigation & Reports List Screen

**Files:**
- Modify: Mobile Navigation Bar config (`frontend/app/(mobile)/layout.tsx` or navigation config)
- Create: `frontend/app/(mobile)/reporting/page.tsx`
- Create: `frontend/app/(mobile)/reporting/components/NewReportModal.tsx`
- Create: `frontend/lib/api/reportingClient.ts`
- Test: `frontend/__tests__/mobile/ReportingPage.test.tsx`

**Interfaces:**
- Consumes: `/api/v1/reporting/reports`
- Produces: Mobile `/mobile/reporting` UI page, `NewReportModal` component

- [ ] **Step 1: Write frontend test for Mobile Reporting Page**

Create `frontend/__tests__/mobile/ReportingPage.test.tsx` testing report listing, filtering, and opening modal.

- [ ] **Step 2: Implement reportingClient.ts**

Create frontend API helper `reportingClient.ts` to call C# backend reporting endpoints.

- [ ] **Step 3: Implement NewReportModal and Reporting page**

Create `NewReportModal.tsx` with inputs for Radiology ID, Patient Name, Age, Gender, and read-only timestamp.
Create `/mobile/reporting/page.tsx`.

- [ ] **Step 4: Run frontend tests**

Run: `pnpm --filter @radiopad/frontend test`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add frontend/app/\(mobile\)/reporting frontend/lib/api/reportingClient.ts frontend/__tests__/mobile
git commit -m "feat(mobile): add Reporting tab list view and New Report modal form"
```

---

### Task 5: Mobile App Audio Dictation Recorder

**Files:**
- Create: `frontend/app/(mobile)/reporting/[id]/dictate/page.tsx`
- Create: `frontend/app/(mobile)/reporting/components/AudioRecorderControls.tsx`
- Test: `frontend/__tests__/mobile/AudioRecorder.test.tsx`

**Interfaces:**
- Consumes: Capacitor Voice Recorder / MediaRecorder API, `reportingClient.uploadDictation`
- Produces: Audio recorder page with Record, Pause, Resume, Stop, Preview, Submit

- [ ] **Step 1: Write AudioRecorder component tests**

Mock `MediaRecorder` / Capacitor audio recording and test Record -> Pause -> Resume -> Stop -> Submit state machine.

- [ ] **Step 2: Implement AudioRecorderControls component**

Build UI with live waveform visualizer, time counter, Record/Pause toggle, Stop button, playback preview, and Submit button.

- [ ] **Step 3: Implement Dictate Page**

Create `frontend/app/(mobile)/reporting/[id]/dictate/page.tsx` integrating recorder controls and calling backend API on Submit.

- [ ] **Step 4: Run test suite**

Run: `pnpm --filter @radiopad/frontend test`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add frontend/app/\(mobile\)/reporting/\[id\]/dictate frontend/__tests__/mobile
git commit -m "feat(mobile): implement audio dictation recorder with pause/resume and submit workflow"
```

---

### Task 6: Desktop App "Positive Findings" Tab Integration & Audio Player

**Files:**
- Create: `frontend/app/(desktop)/reports/[id]/components/PositiveFindingsTab.tsx`
- Create: `frontend/app/(desktop)/reports/[id]/components/DictationAudioCard.tsx`
- Modify: `frontend/app/(desktop)/reports/[id]/ReportClient.tsx`
- Test: `frontend/__tests__/desktop/PositiveFindingsTab.test.tsx`

**Interfaces:**
- Consumes: SignalR `ReportingHub`, `DictationAudioDto`
- Produces: "Positive Findings" tab in desktop report view with audio player & editable transcript text box

- [ ] **Step 1: Write unit tests for PositiveFindingsTab component**

Test dictation card rendering, audio player playback controls, transcript editing, and "Append to Main Findings" handler.

- [ ] **Step 2: Implement DictationAudioCard and PositiveFindingsTab**

Create audio waveform player component, speed controls (`1.0x` to `2.0x`), editable textarea, and "Append to Main Findings" button.
Connect SignalR listener for live incoming mobile dictations.

- [ ] **Step 3: Integrate into ReportClient.tsx**

Add "Positive Findings" tab to Desktop Report view.

- [ ] **Step 4: Run frontend tests**

Run: `pnpm --filter @radiopad/frontend test`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add frontend/app/\(desktop\)/reports/\[id\] frontend/__tests__/desktop
git commit -m "feat(desktop): integrate Positive Findings tab with audio player and real-time SignalR dictation handoff"
```

---

### Task 7: Admin Panel Dictation AI Settings Page

**Files:**
- Create: `frontend/app/(desktop)/admin/dictation-settings/page.tsx`
- Create: `backend/RadioPad.Api/src/RadioPad.Api/Controllers/DictationSettingsController.cs`
- Test: `frontend/__tests__/admin/DictationSettings.test.tsx`

**Interfaces:**
- Consumes: `GET/POST /api/v1/admin/dictation-settings`
- Produces: Admin panel UI for engine selection (`medASR` Default vs `UBAG`), model selection, system prompt tuning, and live test transcription

- [ ] **Step 1: Implement DictationSettingsController in C# backend**

Expose settings management endpoints.

- [ ] **Step 2: Implement Admin Dictation Settings Page**

Build UI with engine radio buttons, model dropdown (`UBAG Gemini Audio` / `UBAG ChatGPT Audio`), radiology prompt textarea, and test audio upload tool.

- [ ] **Step 3: Run tests**

Run: `pnpm --filter @radiopad/frontend test`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add frontend/app/\(desktop\)/admin/dictation-settings backend/RadioPad.Api/src/RadioPad.Api/Controllers/DictationSettingsController.cs
git commit -m "feat(admin): add Dictation & Audio AI Settings page with medASR default and UBAG provider configuration"
```

---

### Task 8: End-to-End Verification & Verification Walkthrough

**Files:**
- Create: Verification script/test runner

- [ ] **Step 1: Run full C# Backend test suite**

Run: `dotnet test backend/RadioPad.Api/RadioPad.Api.sln`
Expected: PASS

- [ ] **Step 2: Run full Frontend test suite**

Run: `pnpm --filter @radiopad/frontend test`
Expected: PASS

- [ ] **Step 3: Commit final verification**

```bash
git commit --allow-empty -m "chore(reporting): verify mobile dictation and positive findings pipeline"
```
