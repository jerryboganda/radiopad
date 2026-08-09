# Mobile Audio Dictation & Positive Findings Reporting Pipeline Design Document

**Date**: 2026-08-09  
**Status**: Approved (Brainstorming Phase Complete)  
**Target Systems**: Mobile App (Capacitor/Next.js), C# Backend (.NET API), Desktop Workstation (Tauri/Next.js)

---

## 1. Executive Summary & Workflow Overview

This feature introduces an integrated mobile-to-desktop dictation pipeline for RadioPad. It enables senior radiologists to quickly record structured voice dictations on their mobile devices (or tablets), which are automatically transcribed using a medical-grade AI engine and pushed in real-time to the desktop workstation's **Positive Findings** tab. Junior radiologists or reporting assistants on desktop can immediately review the audio, edit the transcript, and incorporate it into the final radiology report.

### Core Handoff Workflow
```
[ Senior Radiologist on Mobile ] 
   ├── 1. Opens "Reporting" tab
   ├── 2. Clicks "+ New Report" (Inputs Radiology ID, Patient Name, Age, Gender)
   ├── 3. Records audio dictation (Record -> Pause / Resume -> Stop -> Preview -> Submit)
   └── 4. Uploads payload to C# Backend API
                  │
                  ▼
   [ C# Backend API Orchestrator ]
   ├── Stores raw audio & report metadata
   ├── Runs Transcription Engine (Default: Local medASR 4.4% WER | Configurable: UBAG Gemini/ChatGPT)
   └── Emits SignalR real-time event to Desktop Client
                  │
                  ▼
[ Junior Radiologist on Desktop ]
   ├── 1. Receives real-time notification: "New Dictation Received"
   ├── 2. Opens "Positive Findings" tab under Report view
   ├── 3. Plays audio clip & reviews AI-generated medical transcript
   └── 4. Clicks "Append to Main Findings" to assemble final report
```

---

## 2. Mobile App Architecture (`frontend/app/(mobile)`)

### 2.1 Navigation Bar Update
- Add **Reporting** tab to the bottom mobile shell navigation bar (`/mobile/reporting`).

### 2.2 Reporting Dashboard (`frontend/app/(mobile)/reporting/page.tsx`)
- **Header**: "Reporting" header with a top **"+ New Report"** primary action button.
- **Search & Filter**: Search bar for filtering by `Radiology ID`, `Patient Name`, or status (`All`, `Pending Review`, `Finalized`).
- **Reports List**: Displays active report cards showing:
  - Radiology ID (e.g. `RAD-2026-0891`)
  - Patient Details: Name, Age, Gender (e.g. `John Doe, 45M`)
  - Timestamp (Auto-recorded ISO UTC date & time)
  - Dictation Clip Count & Status Badge (e.g. `2 Audio Clips | medASR Transcribed`)
- Tapping a report opens the **Report Dictation Detail** screen to record additional clips for an existing report.

### 2.3 New Report Form Modal (`NewReportModal.tsx`)
Prompted when clicking **"+ New Report"**:
- `Radiology ID` (String, required)
- `Patient Name` (String, required)
- `Patient Age` (Number, required)
- `Patient Gender` (Select: `Male`, `Female`, `Other`, required)
- `Date & Time` (System auto-filled ISO timestamp, read-only)
- **OK / Next** button navigates directly to the **Audio Dictation Recorder**.

### 2.4 Audio Dictation Recording Screen (`AudioDictationRecorder.tsx`)
- **Patient Header Banner**: Displays `RAD-2026-0891 | John Doe, 45M`.
- **Live Visualizer**: Real-time microphone input volume waveform.
- **Timer**: Active recording counter (`00:00 / 05:00`).
- **Control Bar**:
  - **Record / Pause Toggle**: Allows pausing the dictation mid-thought and resuming into a seamless audio stream.
  - **Stop Button**: Finalizes the recording session.
  - **Playback Preview Bar**: Play/Pause local preview before submitting.
  - **Submit Button**: Uploads audio blob + metadata to `POST /api/v1/reporting/reports/{id}/dictations`.

---

## 3. Backend API & Database Architecture (`backend/RadioPad.Api`)

### 3.1 Domain Models (`RadioPad.Domain/Entities`)

```csharp
public class Report
{
    public Guid Id { get; set; }
    public string RadiologyId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public int PatientAge { get; set; }
    public string PatientGender { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ReportStatus Status { get; set; } = ReportStatus.Draft;
    public ICollection<DictationAudio> Dictations { get; set; } = new List<DictationAudio>();
}

public class DictationAudio
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
    public string TranscriptionEngine { get; set; } = "medASR-6gram";
    public TranscriptionStatus Status { get; set; } = TranscriptionStatus.Pending;
    public string? TranscribedText { get; set; }
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? TranscribedAt { get; set; }
    public string? ErrorMessage { get; set; }
}
```

### 3.2 REST API Endpoints (`ReportingController.cs`)
- `POST /api/v1/reporting/reports`: Creates a new report record.
- `GET /api/v1/reporting/reports`: Fetches paginated/searched list of reports.
- `GET /api/v1/reporting/reports/{id}`: Returns complete report with attached audio dictations.
- `POST /api/v1/reporting/reports/{id}/dictations`: Uploads multipart audio payload (`.wav`, `.webm`, or `.m4a`).

### 3.3 Real-Time Notification (`ReportingHub.cs`)
- SignalR Hub emits `DictationUploaded` and `TranscriptionCompleted` events to desktop subscribers for instantaneous desktop UI updates.

---

## 4. Transcription Engine Architecture

### 4.1 Abstraction (`ITranscriptionEngine.cs`)
```csharp
public interface ITranscriptionEngine
{
    string EngineId { get; }
    Task<TranscriptionResult> TranscribeAsync(Stream audioStream, TranscriptionContext context, CancellationToken ct);
}
```

### 4.2 Engine Options

1. **Local medASR + 6-Gram Model Engine (DEFAULT)**:
   - **Performance**: 4.4% Word Error Rate (WER) tailored specifically for radiology findings.
   - **Execution**: Runs on-device / local server worker process via native bindings. Zero cloud API latency or cost.

2. **UBAG Cloud Provider Engine (`UbagProviderAdapter.cs`)**:
   - **Models**: `UBAG Gemini Audio` / `UBAG ChatGPT Audio`.
   - **Radiology Context System Prompt**:
     ```text
     You are an expert medical radiology transcription assistant.
     The speaker is a senior radiologist dictating positive findings for a diagnostic imaging case.
     Transcribe the audio accurately. Retain medical terminology, anatomical references, dimensions (mm, cm), and diagnostic impressions.
     Do not summarize or alter clinical terms.
     ```

---

## 5. Desktop Workstation Integration (`frontend/app/(desktop)`)

### 5.1 "Positive Findings" Tab (`frontend/app/(desktop)/reports/[id]/page.tsx`)
- Located within the Desktop Report workspace.
- **Dictation Timeline**: Chronological stream of dictation cards uploaded from Mobile.
- **Dictation Card UI**:
  - Senior Radiologist avatar & recording timestamp.
  - Engine Badge (`medASR 4.4% WER` or `UBAG Gemini`).
  - **Audio Player Component**: Play/Pause button, interactive waveform seekbar, speed options (`1.0x`, `1.25x`, `1.5x`, `2.0x`).
  - **Editable Transcript Field**: Textarea pre-populated with AI transcription for immediate editing by the junior radiologist.
  - **"Append to Main Findings" Button**: Copies text into the main draft editor.

---

## 6. Admin Panel Settings (`frontend/app/(desktop)/admin/dictation-settings`)

1. **Active Engine Selector**:
   - `Local medASR + 6-Gram Model (4.4% WER)` (**Default**)
   - `UBAG Cloud AI Provider`
   - `OpenAI Direct Provider`
2. **UBAG Model Engine Config**: `UBAG Gemini Audio` | `UBAG ChatGPT Audio`.
3. **Radiology System Prompt Editor**: Text editor with "Reset to Default" button.
4. **Test Transcription Utility**: Upload/record audio snippet to test live transcription output.

---

## 7. Verification & Acceptance Criteria
- [ ] Mobile app allows creating a new report with Radiology ID, Name, Age, Gender, and system date/time.
- [ ] Mobile recorder supports Record, Pause, Resume, Stop, Preview, and Submit.
- [ ] Default transcription uses local `medASR + 6-gram` model (4.4% WER).
- [ ] Backend supports switching to `UBAG` (Gemini / ChatGPT) with radiology system prompts via Admin Panel.
- [ ] Desktop app receives SignalR real-time push and renders dictation cards with audio player and editable transcripts under "Positive Findings".
