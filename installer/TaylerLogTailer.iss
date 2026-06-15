; Inno Setup script for Tayler Log Tailer.
;
; Produces a per-machine, self-contained x64 installer.  The application is
; published with the .NET 10 runtime bundled, so no separate runtime install
; is required on the target machine.
;
; Build the publish output first (see installer\build-installer.ps1), then
; compile this script with:
;
;     "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\TaylerLogTailer.iss
;
; The version string is read from the published executable, which in turn comes
; from the <Version> property in TaylerLogTailer.csproj - a single source of
; truth.

#define MyAppName "Tayler Log Tailer"
#define MyAppPublisher "Hannah Vernon"
#define MyAppURL "https://code.hannahvernon.com/hannah-vernon/tayler-log-tailer"
#define MyAppExeName "TaylerLogTailer.exe"
#define PublishDir "..\publish\win-x64"
#define MyAppVersion GetFileVersion(AddBackslash(SourcePath) + PublishDir + "\" + MyAppExeName)

[Setup]
AppId={{082C8912-D183-4E6C-8BAF-63C9533F4168}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
LicenseFile=..\LICENSE
SetupIconFile=..\src\TaylerLogTailer\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
OutputDir=..\dist
OutputBaseFilename=TaylerLogTailer-{#MyAppVersion}-setup-x64
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
