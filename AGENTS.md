# CamMicBlocker — Agent Guidelines & Project Instructions

This document provides operational context, architectural rules, coding standards, and build instructions for AI agents working on **CamMicBlocker**.

---

## 1. Project Overview & Target Framework

- **Technology**: C# / .NET 10 / WPF + Windows Forms (System Tray).
- **Architecture**: Layered Domain-Driven Design (`Domain`, `Infrastructure`, `Application`, `UI`, `Logging`).
- **Privilege Model**: **Single-Elevation (`requireAdministrator`)**.
  - `CamMicBlocker.exe` prompts UAC **once at launch**.
  - All subsequent PnP device manipulations (`CfgMgr32.dll`) and registry policy writes (`HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy`) execute **directly in-process** without launching external helper processes or triggering additional UAC prompts.

---

## 2. Solution Structure

```text
Cam&MicroBlocker/
├── CamMicBlocker.sln                 # Main Solution
├── AGENTS.md                         # Agent instructions & context
├── src/
│   ├── CamMicBlocker/                # Primary WPF Desktop & Tray Application
│   │   ├── app.manifest              # Manifest (requireAdministrator)
│   │   ├── appsettings.json          # Configuration (Log levels, retention)
│   │   ├── App.xaml / App.xaml.cs    # Entry point & Granular Startup Logging
│   │   ├── Application/              # Orchestration (BlockingService, HotkeyService, StartupService)
│   │   ├── Domain/                   # Domain Models & Interfaces (IDeviceDetector, IDeviceController, etc.)
│   │   ├── Infrastructure/           # Win32 P/Invoke (CfgMgrInterop, DeviceDetector, PolicyManager, DeviceController, StateStore)
│   │   ├── Logging/                  # Serilog setup & CrashReporter (JSON dumps in %LOCALAPPDATA%\CamMicBlocker\CrashReports\)
│   │   └── UI/                       # WPF Views (MainWindow, NotificationWindow, TrayIconManager)
│   └── CamMicBlocker.Elevated/       # Deprecated helper (kept for reference, in-process execution is now active)
├── tests/
│   └── CamMicBlocker.Tests/          # xUnit Unit Test Suite (26 tests)
└── legacy/                           # Legacy PowerShell scripts (BloqueoCamaraMicrofono_Pro.ps1)
```

---

## 3. Build, Test & Publish Commands

### Restore & Build
```powershell
dotnet build CamMicBlocker.sln
```

### Run Unit Tests
```powershell
dotnet test CamMicBlocker.sln
```

### Run Application (Debug)
```powershell
dotnet run --project src/CamMicBlocker/CamMicBlocker.csproj
```

### Publish Single-File Portable Executable
```powershell
dotnet publish src/CamMicBlocker/CamMicBlocker.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```
*Output path*: `src/CamMicBlocker/bin/Release/net10.0-windows/win-x64/publish/CamMicBlocker.exe`

---

## 4. Key Engineering Conventions & Restrictions

### A. Device Detection Strategy
- **Never match devices by display name regex**. Always query WMI `Win32_PnPEntity` by **Class GUID**:
  - **Camera Class GUID**: `{ca3e7ab9-b4c3-4ae6-8251-579ef933890f}`
  - **Audio Endpoint Class GUID**: `{c166523c-fe0c-4a94-a586-f1a80cfbbf3e}`
- **Microphone Filtering**: Audio endpoints include both capture (microphone) and render (speaker) devices. Filter using instance ID pattern `"{0.0.1."` for capture endpoints.

### B. Privilege & Operations Execution
- Do **not** spawn external helper processes via `Verb="runas"`. The main app is elevated on launch (`requireAdministrator`).
- Execute PnP node operations in-process via `DeviceController` (`CM_Locate_DevNodeW`, `CM_Disable_DevNode`, `CM_Enable_DevNode`).
- Execute registry policy operations in-process via `PolicyManager` (`HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy`).

### C. Observability & Logging Rules
- Framework: **Serilog**.
- Storage: `%LOCALAPPDATA%\CamMicBlocker\Logs\CamMicBlocker-yyyyMMdd.log`.
- Crash Dumps: `%LOCALAPPDATA%\CamMicBlocker\CrashReports\crash-yyyyMMdd-HHmmss-fff.json`.
- **Correlation ID**: Multi-step operations must use `LogContext.PushProperty("OperationId", opId)` with a short GUID (`Op-xxxxxxxx`).
- **Performance Timing**: Measure all PnP, WMI, and IO operations using `System.Diagnostics.Stopwatch` and include `DurationMs` in log events.
- **Exceptions**: Never catch exceptions silently without logging (`catch { }` is prohibited). All unhandled domain, UI dispatcher, and background task exceptions must be passed to `CrashReporter.GenerateCrashReport(...)`.

### D. UI/UX Guidelines
- **Theme**: Fluent Dark (`#181818` background, `#252528` cards, `#007ACC` primary accent).
- **Custom Title Bar**: `WindowStyle="None"`, `AllowsTransparency="True"`, 38px `#1A1A1D` header with custom minimize (`—`) and red close (`✕`) buttons.
- **Custom ScrollBar**: 8px ultra-thin scrollbar track with pill thumb (`CornerRadius="4"`) and `#007ACC` hover transition.
- **Window Lifecycle**: Closing `MainWindow` via `(X)` hides it to the system tray (`e.Cancel = true; Hide();`).
