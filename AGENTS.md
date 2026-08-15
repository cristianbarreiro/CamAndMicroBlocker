# PrivLock — Agent Guidelines, Architecture & Security Rules

This document provides operational context, safety rules, architectural conventions, and engineering guidelines for AI agents working on **PrivLock**.

---

## 1. Multiplatform Safety, Hardware Integrity & Security Policy

### A. Core Security Principles
- **Official Native APIs First**: Always use officially supported platform APIs:
  - **Windows**: `CfgMgr32.dll` PnP node management, `HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy` Group Policies, WMI `Win32_PnPEntity`.
  - **Linux**: V4L2 device node control (`/dev/video*`), `sysfs`/`udev` driver unbind, PipeWire (`wpctl`) and PulseAudio (`pactl`) audio source management, Polkit (`pkexec`).
  - **macOS**: CoreAudio HAL (`AudioObjectSetPropertyData` / `kAudioDevicePropertyMute`), `AVFoundation` camera state inspection, `LaunchAgents` plist.
- **Zero Low-Level Hardware Tampering**: Never attempt to modify hardware firmware, device registers, flash memory, EEPROM, or kernel-mode driver binaries. All hardware protection must be strictly software-level PnP node state toggling, driver unbinds, sound server locks, and OS Group Policy enforcement.
- **Zero Security Bypasses**: Never implement UAC bypasses, bypass driver signature enforcement, disable Windows Defender, bypass macOS SIP / hardened runtime, or alter system security subsystems. Security and system stability take priority over implementation convenience.
- **Fail Securely & Verified State**: Never report a device as "Blocked" unless verified against actual state (`EffectiveStatus`).
- **No Swallowing Errors**: Never catch exceptions silently (`catch { }` is strictly prohibited). Critical driver, PnP, permission, or hardware errors must be logged with context and passed to `CrashReporter`.

### B. Risk Classification & Validation Policy
Validate changes proportionally to their potential impact on system stability:

- **Low-Risk Changes (UI / UX / Localization / ViewModels)**:
  - *Scope*: Avalonia XAML layouts, Fluent dark styling, `LocalizationCatalog` (`StringsEn`/`StringsEs`), tray tooltips, non-blocking UI logic.
  - *Validation*: Build (`dotnet build`) and visual smoke test.

- **High-Risk Changes (PnP / Kernel Driver / Audio HAL / Registry / Privileges / OS Subsystems)**:
  - *Scope*: `PrivLock.Platform.Windows`, `PrivLock.Platform.Linux`, `PrivLock.Platform.MacOS`, `ProtectionService`, `app.manifest`, `Program.cs` startup.
  - *Validation*: Full unit test suite (`dotnet test`), published binary validation (`dotnet publish`), and 9-point risk assessment verification.

---

## 2. High-Risk Change Evaluation Checklist (9-Point Assessment)

Before committing changes to hardware controllers, policy managers, sound servers, or privileged subsystem code on any operating system, evaluate the following 9 failure vectors:

1. **OS Subsystem Impact**: Which subsystem is modified (Windows PnP / Registry, Linux V4L2 / PipeWire / PulseAudio, macOS CoreAudio HAL)?
2. **Partial Failure Handling**: What happens if device 1 disable succeeds but device 2 fails?
3. **Clean Reversibility**: Can the system state be 100% restored if an error occurs mid-operation?
4. **Reboot Resilience**: Will the device or policy state remain consistent after a system restart?
5. **Hardware Disconnection**: How does the code handle a camera or microphone being physically unplugged/replugged mid-operation (*hotplug*)?
6. **Elevation / Permission Denial**: What happens if the user denies elevation (UAC prompt, Polkit dialog, sudo)?
7. **Privilege Loss / Restricted Environment**: Does the application degrade gracefully if running in a sandboxed or non-root environment?
8. **Driver Problem States**: How does the system handle pre-existing problem states (e.g., `CM_PROB_DISABLED` 0x16 on Windows, unmounted sound card on Linux)?
9. **Post-Mortem Observability**: Is sufficient non-sensitive diagnostic data logged to diagnose field failures via structured JSON crash dumps?

---

## 3. Architecture & Privilege Model

### A. Layered Clean Architecture (Domain-Driven Design + Strategy Pattern)
```text
src/
├── PrivLock.Domain/                  # Pure C# domain: Models, Enums, Capabilities, Results
│   ├── Models/                       # BlockStatus, BlockTarget, DeviceType, DeviceInfo, BlockState
│   ├── Capabilities/                 # PlatformCapabilities, CapabilityLevel, PlatformInfo
│   └── Results/                      # OperationResult, DeviceOperationDetail, ElevationResult
│
├── PrivLock.Platform.Abstractions/   # Pure OS contracts and interfaces
│   ├── IDeviceProtectionProvider.cs  # Unified block/unblock/status contract
│   ├── IDeviceDetector.cs            # Camera and microphone hardware enumeration
│   ├── IPlatformCapabilityProvider.cs# Declarative capability matrix query
│   ├── IElevationProvider.cs         # Decoupled elevation detection & request
│   ├── IAutostartProvider.cs         # Operating system autostart management
│   ├── IGlobalHotkeyProvider.cs      # Global hotkey registration
│   ├── ISingleInstanceGuard.cs       # Single instance mutex/lockfile guard
│   └── IStateStore.cs                # State persistence contract
│
├── PrivLock.Infrastructure.Common/   # Cross-platform shared infrastructure
│   ├── Storage/                      # FileStateStore (JSON in OS AppData)
│   ├── Logging/                      # LoggingConfiguration (Serilog) & CrashReporter (JSON dumps)
│   └── Localization/                 # LocalizationCatalog (In-memory bilingual catalogs)
│
├── PrivLock.Application/             # Use cases & orchestration (100% OS & UI agnostic)
│   └── Services/                     # ProtectionService, SettingsService, LocalizationService
│
├── PrivLock.Platform.Windows/        # Windows native implementations
│   ├── Devices/                      # CfgMgr32 P/Invoke & WindowsDeviceDetector (WMI/GUIDs)
│   ├── Policies/                     # HKLM AppPrivacy Registry Policies
│   ├── Elevation/                    # WindowsElevationProvider (UAC / Token)
│   └── System/                       # WindowsAutostartProvider, WindowsHotkeyProvider, WindowsSingleInstanceGuard
│
├── PrivLock.Platform.Linux/          # Linux native implementations
│   ├── Devices/                      # V4L2/sysfs DeviceDetector & PipeWire/PulseAudio Controller
│   ├── Elevation/                    # LinuxElevationProvider (Polkit/pkexec / euid)
│   └── System/                       # LinuxAutostartProvider, LinuxSingleInstanceGuard
│
├── PrivLock.Platform.MacOS/          # macOS native implementations
│   ├── Devices/                      # CoreAudio HAL DeviceDetector & Input Mute Controller
│   ├── Elevation/                    # MacOSElevationProvider (osascript / authorization)
│   └── System/                       # MacOSAutostartProvider, MacOSSingleInstanceGuard
│
├── PrivLock.UI/                      # Multiplatform UI in Avalonia 11
│   ├── Views/                        # MainWindow.axaml with custom Fluent Dark chrome
│   ├── ViewModels/                   # MainViewModel, DeviceItemViewModel
│   └── App.axaml                     # Fluent Dark theme & styles
│
└── PrivLock.Desktop/                 # Executable Host
    ├── Program.cs                    # Platform DI composition root & lifecycle
    └── app.manifest                  # Windows single-elevation manifest
```

### B. Declarative Capabilities Matrix
PrivLock never makes false security promises. Each platform explicitly reports its capability level:
- **Windows**: `CameraProtectionLevel = DualLayer`, `MicrophoneProtectionLevel = DualLayer` (`CfgMgr32` hardware PnP + HKLM Registry Policy).
- **Linux**: `CameraProtectionLevel = Hardware` (V4L2 driver unbind / ACLs), `MicrophoneProtectionLevel = Software` (PipeWire/PulseAudio server source mute & lock).
- **macOS**: `CameraProtectionLevel = Software` (AVFoundation state), `MicrophoneProtectionLevel = Hardware` (CoreAudio HAL hardware input mute).

---

## 4. Technical Conventions & Restrictions

### A. Device Detection Strategy
- **Windows**: Class GUID filtering only (Camera: `{ca3e7ab9-b4c3-4ae6-8251-579ef933890f}`, AudioEndpoint: `{c166523c-fe0c-4a94-a586-f1a80cfbbf3e}` with `{0.0.1.` capture pattern).
- **Linux**: V4L2 nodes in `/sys/class/video4linux/` and ALSA `/proc/asound/pcm` capture endpoints.
- **macOS**: CoreAudio HAL input streams and AVFoundation devices.

### B. Observability & Crash Diagnostics
- **Serilog Structured Logs**:
  - Windows: `%LOCALAPPDATA%\PrivLock\Logs\PrivLock-yyyyMMdd.log`
  - Linux: `~/.local/share/PrivLock/Logs/PrivLock-yyyyMMdd.log`
  - macOS: `~/Library/Application Support/PrivLock/Logs/PrivLock-yyyyMMdd.log`
- **Structured Crash Reports**: JSON dumps written to `.../PrivLock/CrashReports/crash-yyyyMMdd-HHmmss-fff.json`.
- **Correlation**: All operations push an `OperationId` (`Op-xxxxxxxx`) to Serilog `LogContext` and measure execution time with `System.Diagnostics.Stopwatch`.

### C. UI/UX Standards (Avalonia 11)
- **Fluent Dark Theme**: `#181818` background, `#252528` card panels, `#007ACC` primary accent.
- **Custom Integrated Title Bar**: `ExtendClientAreaToDecorationsHint="True"`, 38px `#1A1A1D` header with custom drag, minimize (`—`), and close (`✕`).
- **Dynamic Localization (i18n)**: All UI elements bind to `MainViewModel` localized properties backed by `LocalizationCatalog` / `LocalizationService` (`"es"` / `"en"`).

---

## 5. Build, Test & Publish Commands

### Restore & Build Solution
```powershell
dotnet build CamMicBlocker.sln
```

### Run Full Test Suite (59 Tests)
```powershell
dotnet test CamMicBlocker.sln
```

### Run Multiplatform Desktop App (Debug)
```powershell
dotnet run --project src/PrivLock.Desktop/PrivLock.Desktop.csproj
```

### Publish Single-File Executables

#### Windows x64:
```powershell
dotnet publish src/PrivLock.Desktop/PrivLock.Desktop.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish_out/win-x64
```

#### Linux x64:
```powershell
dotnet publish src/PrivLock.Desktop/PrivLock.Desktop.csproj -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o publish_out/linux-x64
```

#### macOS ARM64 (Apple Silicon):
```powershell
dotnet publish src/PrivLock.Desktop/PrivLock.Desktop.csproj -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true -o publish_out/osx-arm64
```
