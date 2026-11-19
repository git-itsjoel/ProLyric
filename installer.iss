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
// Check whether .NET 8.0 Desktop Runtime (x64) is installed
function IsDotNet8DesktopRuntimeInstalled(): Boolean;
var
  FindRec: TFindRec;
  SharedFxPath: String;
begin
  Result := False;

  // 1. Check in 64-bit dotnet shared directory: C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\8.0.*
  SharedFxPath := ExpandConstant('{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App\8.0*');
  if FindFirst(SharedFxPath, FindRec) then
  begin
    try
      Result := True;
    finally
      FindClose(FindRec);
    end;
    Exit;
  end;

  // 2. Also check with {commonpf64}
  SharedFxPath := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\8.0*');
  if FindFirst(SharedFxPath, FindRec) then
  begin
    try
      Result := True;
    finally
      FindClose(FindRec);
    end;
    Exit;
  end;

  // 3. Check HKLM 64-bit registry for Microsoft.WindowsDesktop.App
  if RegKeyExists(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App') then
  begin
    Result := True;
    Exit;
  end;
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;

  if not IsDotNet8DesktopRuntimeInstalled() then
  begin
    if MsgBox('ProLyric requires the Microsoft .NET 8.0 Desktop Runtime (x64) to run.' + #13#10 + #13#10 +
              'Would you like to open the official download page now to install it?',
              mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', 'https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    end;

    if MsgBox('Would you like to continue installing ProLyric anyway?' + #13#10 + #13#10 +
              '(Note: You will need to install the .NET 8.0 Desktop Runtime before running ProLyric).',
              mbConfirmation, MB_YESNO) = IDNO then
    begin
      Result := False;
    end;
  end;
end;
