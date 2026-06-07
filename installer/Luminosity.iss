; Inno Setup script for Luminosity
; Build with:  "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\Luminosity.iss
; (or wherever ISCC.exe lives). Produces installer\Output\Luminosity-Setup-<ver>.exe
;
; Prerequisite: publish the self-contained single-file exe first:
;   dotnet publish -c Release -r win-x64 --self-contained true ^
;     -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

#define MyAppName "Luminosity"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "Andrei Stefan"
#define MyAppURL "https://github.com/anro772/luminosity"
#define MyAppExeName "Luminosity.exe"
#define MyPublishDir "..\bin\Release\net9.0-windows\win-x64\publish"

[Setup]
AppId={{2444808E-FA9F-4094-A297-37E236AF97E4}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\Assets\icon.ico
OutputDir=Output
OutputBaseFilename=Luminosity-Setup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Self-contained x64 build — no .NET prerequisite. Installs per-user (no admin prompt).
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#MyPublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
