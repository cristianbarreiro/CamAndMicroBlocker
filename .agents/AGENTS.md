# CamMicBlocker — Project Guidelines & Rules

Refer to the primary project documentation in `AGENTS.md` at the workspace root for complete architectural guidelines, build instructions, and observability requirements.

## Quick Summary
- Target: .NET 10 / WPF
- Elevation: `requireAdministrator` in `app.manifest` (single-UAC prompt on startup)
- PnP Control: In-process CfgMgr32 P/Invoke (`DeviceController.cs`)
- Registry Control: In-process HKLM AppPrivacy (`PolicyManager.cs`)
- Logging: Serilog + CrashReporter in `%LOCALAPPDATA%\CamMicBlocker\`
