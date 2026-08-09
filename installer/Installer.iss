; Inno Setup script for Paperbunkr (P7, docs/alpha-todo.md).
;
; Packaging approach follows ComicRackCE's own precedent (_reference/ComicRackCE/Installer.iss),
; but with two deliberate deviations, both decided with the user before writing this file:
;   1. Self-contained publish (dotnet publish -r win-x64 --self-contained) instead of CE's
;      detect-and-download-.NET-Framework-4.8 dance - Paperbunkr bundles its own .NET 8 runtime,
;      so there is no prerequisite-installation [Code] section here at all.
;   2. No [Registry] file-association entries. CE's installer writes the .cbz/.cbr/.../.cbl
;      ProgID keys itself; Paperbunkr's FileAssociationService (src/Paperbunkr.App/Services/
;      FileAssociationService.cs) already does the exact same registry writes live, from
;      Preferences > Advanced, via the already-ported ShellRegister.RegisterFileOpen. Duplicating
;      that here would just be two systems racing to own the same keys.
;
; Build with installer\BuildInstaller.ps1, which runs the publish step and then invokes this
; script - see that file for the expected /DMyAppVersion and /DMyAppSetupFile parameters.

#define MyAppName "Paperbunkr"
#ifndef MyAppVersion
#define MyAppVersion "0.1.0-alpha"
#endif
#ifndef MyAppSetupFile
#define MyAppSetupFile "PaperbunkrSetup"
#endif
#define MyAppPublisher "Paperbunkr Project"
#define MyAppExeName "Paperbunkr.App.exe"
#define PublishDir "publish\win-x64"

[Setup]
; Generated once for this project - do not reuse for any other installer, and do not regenerate
; on future builds (that would make Windows treat every release as a different, unrelated app for
; upgrade/uninstall purposes).
AppId={{E6F64297-BD9E-4341-A156-5C65C2E6C1DB}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
; Per-machine install (Program Files, admin/UAC required) - matches CE's own PrivilegesRequired,
; chosen over a per-user install even though Paperbunkr's own file-association writes don't need
; elevation (see the file-header note above).
PrivilegesRequired=admin
OutputDir=Output
OutputBaseFilename={#MyAppSetupFile}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\src\Paperbunkr.App\Assets\paperbunkr.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Types]
Name: "full";    Description: "Full installation";
Name: "compact"; Description: "Compact installation";
Name: "custom";  Description: "Custom installation"; Flags: iscustom

[Components]
Name: "app";        Description: "{#MyAppName} (Required)";   Types: full compact custom; Flags: fixed
Name: "start_menu"; Description: "Start Menu shortcut";        Types: full compact
Name: "desktop";     Description: "Desktop shortcut";           Types: full

[Files]
; The entire self-contained publish output (see installer\BuildInstaller.ps1) - exe, every
; managed dll, and the native dependencies (7z.dll under x64\, pdfium.dll, LibHeifSharp.dll, the
; SQLite provider, etc.) all land flat/nested exactly as dotnet publish produced them, so this one
; recursive copy is enough; nothing here is hand-maintained per-file the way CE's list is.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: app

[Registry]
; "App Paths" only - lets the Windows Run dialog and shell resolve the exe by name. Deliberately
; NOT registering any file-extension ProgID here; see the file-header note above for why.
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\{#MyAppExeName}"; ValueType: string; Flags: uninsdeletevalue; ValueData: "{app}\{#MyAppExeName}"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}";                Components: start_menu
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"; Components: start_menu
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}";          Components: desktop

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
