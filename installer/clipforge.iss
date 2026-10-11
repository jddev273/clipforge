; ClipForge installer (Inno Setup 6).
; Installs the Fast-profile (framework-dependent) publish output. Requires the
; .NET 8 desktop runtime; the installer offers to fetch it when missing.
; Built on the release machine: place the publish output next to this file as
;   .\app\  (the contents of bin\Release\publish)
; and run:  iscc clipforge.iss

#define MyAppName "ClipForge"
#define MyAppVersion "0.2.0"
#define MyAppPublisher "jddev273"
#define MyAppURL "https://github.com/jddev273/clipforge"
#define MyAppExeName "ClipForge.exe"

[Setup]
AppId={{a6e6039c-14fe-4a1d-87fc-c323b2b31fa0}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
OutputDir=.
OutputBaseFilename=ClipForge-Setup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "Start ClipForge when I sign in"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]
Source: "app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: autostart; \
    Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; \
    Flags: nowait postinstall skipifsilent

[Code]
{ Check for the .NET 8 desktop runtime; offer the download page when missing. }
function IsDotNet8DesktopInstalled(): Boolean;
var
  Key: String;
begin
  Result := RegKeyExists(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App');
  if not Result then
    Result := RegKeyExists(HKLM32, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App');
end;

function InitializeSetup(): Boolean;
var
  Answer: Integer;
  ErrorCode: Integer;
begin
  Result := True;
  if not IsDotNet8DesktopInstalled() then
  begin
    Answer := MsgBox(
      'ClipForge needs the .NET 8 Desktop Runtime (x64), which was not found on this machine.' + #13#10#13#10 +
      'Download and install it first, then run this setup again. Open the download page now?',
      mbConfirmation, MB_YESNO);
    if Answer = IDYES then
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/8.0', '', '', SW_SHOW, ewNoWait, ErrorCode);
    Result := False;
  end;
end;

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
