# CamMicBlocker — Project Guidelines & Security Rules

Refer to the primary project documentation in `AGENTS.md` at the workspace root for complete architectural guidelines, build instructions, 9-point risk assessment checklists, and security policies.

## Quick Summary
- Target: .NET 10 / WPF
- Security Policy: Official Windows APIs only (`CfgMgr32.dll` PnP & HKLM AppPrivacy). No hardware firmware modification, low-level registry hacks, or UAC bypasses.
- Elevation: `requireAdministrator` in `app.manifest` (single-UAC prompt on startup).
- PnP Control: In-process CfgMgr32 P/Invoke (`DeviceController.cs`).
- Registry Control: In-process HKLM AppPrivacy (`PolicyManager.cs`).
- Detection: Class GUID WMI queries (`{ca3e7ab9-b4c3-4ae6-8251-579ef933890f}` for cameras, `{c166523c-fe0c-4a94-a586-f1a80cfbbf3e}` with `{0.0.1.` for mic capture).
- Logging: Serilog + CrashReporter in `%LOCALAPPDATA%\CamMicBlocker\`.
- Localization: Dynamic XAML ResourceDictionaries (`Strings.es.xaml` / `Strings.en.xaml`).
