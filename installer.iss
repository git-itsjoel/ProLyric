; Inno Setup Script for ProLyric
; Author: Joel Boban

#define MyAppName "ProLyric"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Joel Boban"
#define MyAppExeName "ProLyric.exe"

[Setup]
; App Identity
AppId={{C24B8B4D-4F2A-4C21-9541-19B6F3E6D88A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes

; Output Configuration
OutputDir=dist
OutputBaseFilename=ProLyric-Setup
SetupIconFile=ProLyric\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

; Modern Compression & UI
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern

; 64-bit Configuration (for win-x64 build)
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
DisableProgramGroupPage=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "dist\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
// ProLyric is published as a self-contained application including the .NET runtime.
// No external runtime installation is required.
function InitializeSetup(): Boolean;
begin
  Result := True;
end;
