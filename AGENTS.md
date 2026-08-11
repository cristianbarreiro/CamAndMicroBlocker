# CamMicBlocker — Agent Guidelines, Architecture & Security Rules

This document provides operational context, safety rules, architectural conventions, and engineering guidelines for AI agents working on **CamMicBlocker**.

---

## 1. Safety, Hardware Integrity & Security Policy

### A. Core Security Principles
- **Official Windows APIs First**: Always use standard, officially supported Windows APIs (e.g., `CfgMgr32.dll` PnP node management, `HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy` Group Policies, WMI `Win32_PnPEntity`).
- **Zero Low-Level Hardware Tampering**: Never attempt to modify hardware firmware, device registers, flash memory, EEPROM, or kernel-mode driver binaries. All hardware protection must be strictly software-level PnP node state toggling and Group Policy enforcement.
- **Zero Security Bypasses**: Never implement UAC bypasses, bypass driver signature enforcement, disable Windows Defender, or alter Windows Security subsystems. Security and system stability take priority over implementation convenience.
- **No Swallowing Errors**: Never catch exceptions silently (`catch { }` is strictly prohibited). Critical driver, PnP, permission, or hardware errors must be logged with context and passed to `CrashReporter`.

### B. Risk Classification & Validation Policy
Validate changes proportionally to their potential impact on system stability:

- **Low-Risk Changes (UI / UX / Localization)**:
  - *Scope*: XAML layouts, Fluent dark styling, ResourceDictionaries (`Strings.es.xaml`/`Strings.en.xaml`), tray tooltips, non-blocking UI logic.
  - *Validation*: Build (`dotnet build`) and visual smoke test.

- **High-Risk Changes (PnP / Driver / Registry / Privileges / OS Subsystems)**:
  - *Scope*: `DeviceController`, `PolicyManager`, `DeviceDetector`, `CfgMgrInterop`, `app.manifest`, `App.xaml.cs` startup.
  - *Validation*: Full unit test suite (`dotnet test`), published binary validation (`dotnet publish`), and 9-point risk assessment verification.

---

## 2. High-Risk Change Evaluation Checklist (9-Point Assessment)

Before committing changes to PnP node manipulation, Registry policy management, or privileged subsystem code, evaluate the following 9 failure vectors:

1. **OS Subsystem Impact**: Which Windows subsystem (PnP Manager, Registry Engine, Audio Endpoint Builder) is modified?
2. **Partial Failure Handling**: What happens if `CM_Disable_DevNode` succeeds on device 1 but fails on device 2?
3. **Clean Reversibility**: Can the system state be 100% restored if an error occurs mid-operation?
4. **Reboot Resilience**: Will the PnP device or Registry policy state remain consistent after a Windows restart?
5. **Hardware Disconnection**: How does the code handle a camera or microphone being physically unplugged/replugged mid-operation?
6. **UAC / Permission Cancellation**: What happens if the user denies elevation or HKLM registry write access?
7. **Privilege Loss / Access Denied**: Does the application degrade gracefully if running in a restricted process environment?
8. **Driver Problem States**: How does the system handle pre-existing driver problem states (e.g., `CM_PROB_DISABLED` 0x16)?
9. **Post-Mortem Observability**: Is sufficient non-sensitive diagnostic data logged to diagnose field failures?

---

## 3. Architecture & Privilege Model

### A. Layered Domain-Driven Design
```text
src/CamMicBlocker/
├── app.manifest              # Single-elevation manifest (requireAdministrator)
├── appsettings.json          # Serilog levels, retention rules
├── App.xaml / App.xaml.cs    # Application bootstrapper & Startup logging
├── Application/              # Orchestration (BlockingService, HotkeyService, StartupService, LanguageService)
├── Domain/                   # Pure Models & Interfaces (IDeviceDetector, IDeviceController, IPolicyManager, IStateStore)
├── Infrastructure/           # Win32 P/Invoke (CfgMgrInterop, DeviceDetector, PolicyManager, DeviceController, StateStore)
├── Logging/                  # Serilog setup & CrashReporter (JSON dumps)
└── UI/                       # WPF Views (MainWindow, NotificationWindow, TrayIconManager, Resources/Localization)
```

### B. Single-Elevation Model (`requireAdministrator`)
- `CamMicBlocker.exe` prompts UAC **once at startup**.
- PnP node operations (`CM_Disable_DevNode` / `CM_Enable_DevNode`) and HKLM policy writes (`LetAppsAccessCamera` / `LetAppsAccessMicrophone`) execute **directly in-process** via P/Invoke and `RegistryKey`.
- **Zero UAC Fatigue**: Toggling state via global hotkey (`Ctrl+Alt+B`), system tray menu, or UI switches occurs instantly (0ms IPC latency) with zero subsequent UAC prompts.

---

## 4. Technical Conventions & Restrictions

### A. Device Detection Strategy
- **Class GUID Filtering Only**: Never match devices by display name regex. Query WMI `Win32_PnPEntity` strictly by Class GUID:
  - **Camera Class GUID**: `{ca3e7ab9-b4c3-4ae6-8251-579ef933890f}`
  - **Audio Endpoint Class GUID**: `{c166523c-fe0c-4a94-a586-f1a80cfbbf3e}`
- **Audio Capture Filtering**: Audio endpoints include both speakers (render) and microphones (capture). Filter capture endpoints using the MMDevice instance ID pattern `"{0.0.1."`.

### B. Observability & Crash Diagnostics
- **Serilog Logging**: Written to `%LOCALAPPDATA%\CamMicBlocker\Logs\CamMicBlocker-yyyyMMdd.log`.
- **Crash Dumps**: Structured JSON post-mortem reports saved to `%LOCALAPPDATA%\CamMicBlocker\CrashReports\crash-yyyyMMdd-HHmmss-fff.json`.
- **Correlation & Metrics**: Multi-step operations must push an `OperationId` GUID (`Op-xxxxxxxx`) and record execution timing (`DurationMs`) using `System.Diagnostics.Stopwatch`.

### C. UI/UX & Localization Standards
- **Fluent Dark Theme**: `#181818` background, `#252528` card panels, `#007ACC` primary accent.
- **Custom Integrated Title Bar**: `WindowStyle="None"`, `AllowsTransparency="True"`, 38px `#1A1A1D` header with custom drag, minimize (`—`), and red close (`✕`) buttons.
- **Custom ScrollBar**: 8px ultra-thin track with rounded pill thumb (`CornerRadius="4"`) and `#007ACC` hover transition.
- **Window Lifecycle**: Closing `MainWindow` via `(X)` cancels close and hides to system tray (`e.Cancel = true; Hide();`).
- **Dynamic Localization (i18n)**: All UI text binds via `{DynamicResource Key}` using WPF `ResourceDictionary` files (`Strings.es.xaml`, `Strings.en.xaml`). Language state persists in `state.json` (`Language: "es"` or `"en"`).

---

## 5. Build, Test & Publish Commands

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
dotnet publish src/CamMicBlocker/CamMicBlocker.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish_out
```
*Output path*: `publish_out\CamMicBlocker.exe`
