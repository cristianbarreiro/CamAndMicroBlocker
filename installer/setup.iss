; =====================================================================
; PrivLock — Inno Setup Script
; Professional Windows Desktop Installer with Hardware Integrity & Admin UAC
; =====================================================================

#define MyAppName "PrivLock"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "PrivLock"
#define MyAppExeName "PrivLock.exe"
#define MyAppId "{{8E0F7A12-BFB3-4FE8-B9A5-48FD50A15A9A}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline
OutputBaseFilename=PrivLock-Setup-1.0.0
SetupIconFile=..\src\CamMicBlocker\UI\Resources\Icons\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes

; Ensure running instances are safely closed before installing/updating/uninstalling
CloseApplications=force
CloseApplicationsFilter={#MyAppExeName}

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish_out\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: postinstall nowait skipifsilent shellexec

[Code]
// Pre-uninstall hook: Unblock hardware devices & policies to restore system safety
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
  ExePath: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    ExePath := ExpandConstant('{app}\{#MyAppExeName}');
    if FileExists(ExePath) then
    begin
      Log('Executing pre-uninstall safety cleanup: --unblock-and-exit');
      Exec(ExePath, '--unblock-and-exit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end;
  end;
end;
