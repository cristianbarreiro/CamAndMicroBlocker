<p align="center">
  <img src="assets/logo/cammicroblocker_logo.png" alt="PrivLock Logo" width="128" />
  <h1 align="center">PrivLock — Cross-Platform Camera &amp; Microphone Blocker</h1>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Platform-Windows%20%7C%20Linux%20%7C%20macOS-0078D4?style=for-the-badge&logo=windows&logoColor=white" alt="Platform Support" />
  <img src="https://img.shields.io/badge/Framework-.NET%2010.0%20%7C%20Avalonia%20UI-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 10 & Avalonia UI" />
  <img src="https://img.shields.io/badge/Security-Least%20Privilege%20%7C%20On--Demand%20Elevation-4CAF50?style=for-the-badge&logo=security&logoColor=white" alt="Security" />
  <img src="https://img.shields.io/badge/License-GNU%20GPLv3-0078D4?style=for-the-badge&logo=gnu" alt="GPLv3 License" />
  <img src="https://img.shields.io/badge/Language-Español%20%7C%20English-007ACC?style=for-the-badge" alt="i18n Support" />
</p>

**PrivLock** is a native, high-performance security utility for **Windows**, **Linux**, and **macOS** (built with C# / .NET 10 and Avalonia UI), designed to **reliably, quickly, and 100% reversibly block and unblock access to your camera and microphone**.

PrivLock adheres to the **Principle of Least Privilege**: it runs as **one single application** with standard user permissions by default, elevating privileges **only on-demand** when an administrative action is specifically executed.

---

## 📦 Releases & Downloads

<div align="center">

| Platform | Format | Architecture | Download Link |
| :--- | :---: | :---: | :---: |
| 🪟 **Windows** | Portable Single-File / Setup | `win-x64`, `win-arm64` | [Download Windows Release](https://github.com/cristianbarreiro/PrivLock/releases) |
| 🐧 **Linux** | Single-File Executable + `.desktop` | `linux-x64`, `linux-arm64` | [Download Linux Release](https://github.com/cristianbarreiro/PrivLock/releases) |
| 🍎 **macOS** | Native Application Bundle (`.app`) | `osx-arm64` (Apple Silicon), `osx-x64` | [Download macOS Release](https://github.com/cristianbarreiro/PrivLock/releases) |

</div>

---

## ✨ Key Features

- 🛡️ **Native Multi-Layer Protection by Platform**:
  - **Windows (Dual-Layer)**:
    1. *System Policy Layer*: Enforces group policies in `HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy` for Store and Desktop apps.
    2. *Hardware Controller Layer*: Calls Win32 `CfgMgr32.dll` (`CM_Disable_DevNode` / `CM_Enable_DevNode`) directly to disable PnP device nodes.
  - **Linux (V4L2 + PipeWire / PulseAudio)**:
    1. *Camera*: Device node permission revocation (`/dev/video*`) and USB driver unbinding via `sysfs`/`udev`.
    2. *Microphone*: Direct source mute and lock via WirePlumber/PipeWire (`wpctl`) and PulseAudio (`pactl`) — *requires no root elevation*.
  - **macOS (CoreAudio HAL)**:
    1. *Microphone*: Direct hardware input mute and master volume clamp via CoreAudio HAL (`AudioObjectSetPropertyData`).
    2. *Camera*: TCC permission inspection and active stream verification.
- ⚡ **Single Application & Dynamic On-Demand Elevation**:
  - Starts as a standard user process (`asInvoker`).
  - When a privileged operation is requested, PrivLock triggers the OS authorization (Windows UAC, Linux Polkit, macOS Authorization) **strictly on-demand**.
  - No separate `PrivLock.Elevated.exe` binary — everything is self-contained.
- 🎯 **Transparent Capabilities Model**:
  - PrivLock explicitly reports what each operating system supports without false security claims.
- 🎨 **Modern Fluent Dark UI Design**:
  - Avalonia UI 11 with integrated custom title bar (38px), rounded card containers, and responsive layout.
- 🌐 **Dynamic Multilingual Support (ES / EN)**:
  - Real-time segmented `[ ES | EN ]` language switcher with zero app restart needed.
- ⌨️ **Global Keyboard Shortcut**: Toggle instant protection at any time with **`Ctrl + Alt + B`**.
- 📌 **System Tray & Autostart Integration**:
  - Minimizes seamlessly to the system notification area on close `(X)`.
  - Native autostart support across Windows (`Run` key), Linux (`~/.config/autostart`), and macOS (`LaunchAgents`).

---

## 📐 Clean Architecture & Project Structure

The codebase is organized following **Clean Architecture (Domain-Driven Design + Strategy Pattern)**:

```text
PrivLock/
├── src/
│   ├── PrivLock.Domain/                  # Pure C# domain models, capabilities, and value objects
│   ├── PrivLock.Platform.Abstractions/   # Platform contracts (IDeviceProtectionProvider, IDeviceDetector, etc.)
│   ├── PrivLock.Infrastructure.Common/   # Cross-platform JSON state store, Serilog logging, CrashReporter
│   ├── PrivLock.Application/             # Orchestration services (ProtectionService, Settings, Localization)
│   ├── PrivLock.Platform.Windows/        # Windows CfgMgr32 PnP, WMI GUIDs, HKLM Registry policies, on-demand UAC
│   ├── PrivLock.Platform.Linux/          # Linux V4L2 device nodes, PipeWire/PulseAudio source control
│   ├── PrivLock.Platform.MacOS/          # macOS CoreAudio HAL input mute, AVFoundation, LaunchAgents
│   ├── PrivLock.UI/                      # Multiplatform Avalonia UI 11 Views & ViewModels
│   └── PrivLock.Desktop/                 # Single Executable Host & Platform Dependency Injection
│
├── tests/
│   ├── PrivLock.Domain.Tests/            # Domain unit tests
│   ├── PrivLock.Infrastructure.Tests/    # Storage, crash reporting & localization tests
│   ├── PrivLock.Application.Tests/       # Orchestration, on-demand elevation & business logic tests
│   └── CamMicBlocker.Tests/              # Compatibility test suite
│
└── .github/workflows/
    └── ci.yml                            # GitHub Actions CI matrix (Windows, Ubuntu, macOS)
```

---

## 💻 Building from Source

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### 1. Clone & Build Solution
```powershell
git clone https://github.com/cristianbarreiro/PrivLock.git
cd PrivLock
dotnet build CamMicBlocker.sln
```

### 2. Run Test Suite (61 Tests)
```powershell
dotnet test CamMicBlocker.sln
```

### 3. Run Application (Debug)
```powershell
dotnet run --project src/PrivLock.Desktop/PrivLock.Desktop.csproj
```

### 4. Publish Single-File Executables

```powershell
# Windows x64:
dotnet publish src/PrivLock.Desktop/PrivLock.Desktop.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish_out/win-x64

# Linux x64:
dotnet publish src/PrivLock.Desktop/PrivLock.Desktop.csproj -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o publish_out/linux-x64

# macOS ARM64 (Apple Silicon):
dotnet publish src/PrivLock.Desktop/PrivLock.Desktop.csproj -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true -o publish_out/osx-arm64
```

---

## 🛡️ Security & Observability

- **Least Privilege Architecture**: Runs standard user permissions by default (`asInvoker`), elevating privileges transiently (~50ms) only during privileged operations.
- **Structured Diagnostic Logs**:
  - Windows: `%LOCALAPPDATA%\PrivLock\Logs\PrivLock-yyyyMMdd.log`
  - Linux: `~/.local/share/PrivLock/Logs/PrivLock-yyyyMMdd.log`
  - macOS: `~/Library/Application Support/PrivLock/Logs/PrivLock-yyyyMMdd.log`
- **Post-Mortem Crash Reports**: Structured JSON reports generated in `.../PrivLock/CrashReports/` on unhandled exceptions.
- **Fail-Secure Architecture**: Devices are verified against physical hardware state (`EffectiveStatus`) after every operation.

---

## 📄 License

PrivLock is free and open-source software released under the **GNU General Public License v3.0 (GPL-3.0)**.

```text
Copyright (C) 2026 Cristian Barreiro
Repository: https://github.com/cristianbarreiro/PrivLock.git
```

For complete license terms, legal notices, and third-party dependency disclosures, see [LICENSE](LICENSE) and [COPYRIGHT](COPYRIGHT).