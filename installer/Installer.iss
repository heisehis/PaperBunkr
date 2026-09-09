; Inno Setup script for Paperbunkr (P7, docs/alpha-todo.md).
;
; Packaging approach follows ComicRackCE's own precedent (_reference/ComicRackCE/Installer.iss),
; but with deliberate deviations, decided with the user before writing this file (or added in the
; 2026-09-01 full-customization pass, noted per section below):
;   1. Self-contained publish (dotnet publish -r win-x64 --self-contained) instead of CE's
;      detect-and-download-.NET-Framework-4.8 dance - Paperbunkr bundles its own .NET 8 runtime,
;      so there is no prerequisite-installation [Code] section here at all.
;   2. File-association registration is opt-in via the per-format "associate<fmt>" [Tasks] entries
;      below (one checkbox per comic format: .pdf .cbz .cbr .cb7 .cbt .cbw .djvu), and even then
;      the installer does NOT hand-write the ProgID/extension registry keys itself the way CE's
;      does. It shells out to Paperbunkr.App.exe --register-file-associations <ext>, which runs
;      FileAssociationService - the exact same live registry-write path Preferences > Advanced
;      uses - so there is only ever one place that knows the current extension list and one owner
;      of those registry keys, not two systems racing to own them. (The old single "associate"
;      checkbox looped that CLI over EVERY engine-registered format, which included generic .zip /
;      .rar / .7z - an over-association bug fixed in the 2026-09-09 installer redesign, decision 5.)
;   3. No .NET-Framework/VC++-redist [Code] section (see #1) - what CE's [Code] section is spent
;      on here instead is (a) an opt-in "delete my library data" prompt on uninstall, and (b) an
;      existing-install gate in InitializeSetup (Repair / Uninstall / version-upgrade), modelled
;      on how the VC++ Redistributable installers behave when re-run (2026-09-09 redesign,
;      decision 7).
;
; Build with installer\BuildInstaller.ps1, which runs the publish step (and generates
; publish\WhatsNew.txt, publish\License.txt and publish\InfoAfter.txt - see InfoBeforeFile /
; LicenseFile / InfoAfterFile below) then invokes this script - see that file for the expected
; /DMyAppVersion and /DMyAppSetupFile parameters.

#define MyAppName "Paperbunkr"
#ifndef MyAppVersion
#define MyAppVersion "0.3.0-beta"
#endif
; Pure x.y.z.w form for VersionInfoVersion (the setup exe's own file-version resource) - the
; "-beta" suffix in MyAppVersion is not a valid VS_FIXEDFILEINFO version. Must track
; src/Paperbunkr.App/Paperbunkr.App.csproj's <Version> (0.3.0.0), which is the project-wide
; single source of truth per that file's own comment.
#define MyAppVersionNumeric "0.3.0.0"
#ifndef MyAppSetupFile
#define MyAppSetupFile "PaperbunkrSetup"
#endif
#define MyAppPublisher "Paperbunkr Project"
#define MyAppExeName "Paperbunkr.App.exe"
#define MyAppURL "https://github.com/heisehis/PaperBunkr"
#define PublishDir "publish\win-x64"

[Setup]
; Generated once for this project - do not reuse for any other installer, and do not regenerate
; on future builds (that would make Windows treat every release as a different, unrelated app for
; upgrade/uninstall purposes).
AppId={{E6F64297-BD9E-4341-A156-5C65C2E6C1DB}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
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
; "modern dynamic": modern look, and Setup checks the Windows light/dark theme at launch and
; switches which wizard image/color directives it draws from (the *DynamicDark set below).
; "dynamic" needs Inno 6.4+; the installed compiler is 6.7.3 (confirmed via the Windows uninstall
; registry). No named built-in palette (polar/slate/stellar/windows11/zircon) is used - none match
; the brand's orange/amber, so the personality comes from the image, not the wizard chrome.
WizardStyle=modern dynamic
; Real bug, found via manual testing: Inno Setup 6 defaults DisableWelcomePage to "yes" (skips
; straight to Select Components) - without this, the whole WelcomeLabel1/WelcomeLabel2 personality
; text below never actually gets shown to anyone.
DisableWelcomePage=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\src\Paperbunkr.App\Assets\paperbunkr.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
; Setup exe's own Explorer "Properties > Details" tab.
VersionInfoVersion={#MyAppVersionNumeric}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup
VersionInfoCopyright=Paperbunkr Project, AGPLv3
; Wizard branding (Welcome/Finished page side image + the small badge on every other page) -
; the hexagon-"P" emblem from the app's own logo (src/Paperbunkr.App/Assets/paperbunkr-logo-source.png),
; per user direction to give the installer some personality rather than Inno's stock gray gradient.
; See docs/superpowers/specs/2026-09-09-installer-redesign-design.md decision 1 for the full
; rationale; the short version:
;   - Inno 6.7.3 takes PNG (with alpha) directly for both image slots - no BMP step.
;   - One genuinely-transparent PNG per slot composites correctly on both the light and dark
;     panel backgrounds, so the light and *DynamicDark directives point at the SAME two files.
;   - WizardImage.png is the full emblem+wordmark lockup; WizardSmallImage.png is the bare emblem
;     (the wordmark is illegible at the 58-159px corner-badge size).
;   - WizardImageStretch=no: the lockup is ~square (1.06:1); the side panel is tall/narrow
;     (164:314). Stretching would visibly distort it - centre at natural size instead, letterboxed
;     by the panel background (reads as intentional framing on a transparent PNG).
;   - Backgrounds carry the brand, split by system theme, using the app's own real Default skin
;     colour (src/Paperbunkr.App/Assets/Skins/default/theme.json, #0A0B0D):
;       * dark  -> full dark chrome (page + image panel both #0A0B0D).
;       * light -> middle ground: only the logo panel goes dark/branded; the rest of the wizard
;         stays Inno's native light look (no app "light+amber" skin exists to ground a full
;         light chrome in).
;   - Button/control accent colours stay Inno's default: recolouring them needs a Delphi-built
;     .vsf VCL style file, and that tooling isn't available here (decision 1, "Resolved").
WizardImageFile=Assets\WizardImage.png
WizardImageFileDynamicDark=Assets\WizardImage.png
WizardSmallImageFile=Assets\WizardSmallImage.png
WizardSmallImageFileDynamicDark=Assets\WizardSmallImage.png
WizardImageStretch=no
; Light mode: logo panel only.
WizardImageBackColor=#0A0B0D
; Dark mode: whole wizard.
WizardBackColorDynamicDark=#0A0B0D
; "none" keeps the PNG's own transparency (the page background is already #0A0B0D in dark mode).
WizardImageBackColorDynamicDark=none
; License-acceptance page (shown before the install-directory step), matching CE's own
; LicenseFile precedent. Generated by BuildInstaller.ps1 as a single plain-text document
; combining LICENSE (AGPLv3) + TERMS.md - Inno's LicenseFile is one accept-gate for one file, and
; TERMS.md's opening line ("By downloading, installing, or running Paperbunkr... you agree...") is
; exactly the sort of thing that should be backed by a real accept click, not just an About
; screen. See docs/superpowers/specs/2026-09-09-installer-redesign-design.md decision 3.
LicenseFile=publish\License.txt
; Pre-install "what's new" page - generated by BuildInstaller.ps1 from CHANGELOG.md's latest entry
; (plain text, not the raw markdown - Inno's info-file viewer has no markdown rendering, so showing
; CHANGELOG.md directly would put literal "##"/"-" characters in front of the user). Falls back to a
; one-line placeholder if run outside BuildInstaller.ps1 (e.g. a bare ISCC invocation), so this key
; never points at a missing file.
InfoBeforeFile=publish\WhatsNew.txt
; Post-install quick-start page (shown after file copy, before Finish) - parallel to InfoBeforeFile.
; Static content written verbatim by BuildInstaller.ps1 (unlike WhatsNew.txt it doesn't depend on
; build-time state): where Preferences lives, Preferences > Libraries for a first folder, and the
; wiki link. Design decision 4.
InfoAfterFile=publish\InfoAfter.txt
; Explicit (rather than relying on Inno's default) so the Repair / version-upgrade paths in the
; [Code] InitializeSetup gate below land back in the previously-used install directory - see
; design decision 7 ("falling through to the normal wizard").
UsePreviousAppDir=yes
; NetSparkle-driven auto-update (src/Paperbunkr.App/Services/UpdateService.cs) runs this same
; installer over a copy of Paperbunkr that may still be mid-shutdown when Setup starts (NetSparkle
; exits the app and launches Setup, but doesn't wait for the OS to release file locks). Restart
; Manager integration below detects the app still holding {app}'s files open and closes it, instead
; of failing the file copy - the [Run] "launch after install" entry on the Finished page handles
; getting it running again, so RestartApplications is off to avoid a double-launch.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
; Custom Welcome-page copy (user direction: "give the installer personality") - Inno Setup's
; %n is a literal line break within a [Messages] string, not a placeholder needing escaping.
WelcomeLabel1=Welcome to the {#MyAppName} Beta!
WelcomeLabel2=Get ready to get bunked!%n%nYou're about to install {#MyAppName} {#MyAppVersion} on this computer.%n%nA few things before you dive in:%n     -  This is an early beta - expect rough edges and the occasional bug.%n     -  A ComicRack-inspired comic && manga library and reader, built fresh from scratch.%n     -  Fully self-contained - no separate .NET install needed.%n     -  Your library and settings stay local; nothing leaves this machine.%n     -  Found something broken? We'd love to hear about it.%n%nClick Next to continue, or Cancel to make a quiet escape.
; Custom Finished-page copy, matching the Welcome page's personality (user direction, same as above).
FinishedLabel={#MyAppName} has been tucked into place on this computer.%n%nClick Finish to close this wizard.

[Types]
Name: "full";    Description: "Full installation";
Name: "compact"; Description: "Compact installation";
Name: "custom";  Description: "Custom installation"; Flags: iscustom

[Components]
Name: "app";        Description: "{#MyAppName} (Required)";   Types: full compact custom; Flags: fixed
Name: "start_menu"; Description: "Start Menu shortcut";        Types: full compact
Name: "desktop";     Description: "Desktop shortcut";           Types: full

[Tasks]
; All off by default (Flags: unchecked) - nothing here is needed to use Paperbunkr, and file
; associations in particular are deliberately opt-in (see the file-header note on deviation #2):
; Preferences > Advanced offers the same per-format toggle post-install for anyone who skips this.
Name: "launchatstartup"; Description: "Launch {#MyAppName} when Windows starts"; GroupDescription: "Additional options:"; Flags: unchecked
; One task per comic-specific format, replacing the old single "associate" checkbox
; (docs/superpowers/specs/2026-09-09-installer-redesign-design.md decision 5, + the 2026-09-09
; correction that keeps .cbt for ComicRack CE parity). The old single task looped
; Program.cs's --register-file-associations over EVERY format the engine registers, which
; included generic "ZIP Archive" (.zip), "RAR Archive" (.rar) and "7z Archive" (.7z) - i.e. it
; hijacked bare archive extensions. That over-association is now fixed at the source: the CLI
; path is clamped to FileAssociationService.ComicAssociationExtensions and each task passes its
; own extension. ".cbr" covers both the RAR and RAR5 provider registrations (both claim .cbr).
Name: "associatepdf";  Description: ".pdf  (PDF documents)";           GroupDescription: "Associate comic file types with {#MyAppName}:"; Flags: unchecked
Name: "associatecbz";  Description: ".cbz  (comic book ZIP archive)";  GroupDescription: "Associate comic file types with {#MyAppName}:"; Flags: unchecked
Name: "associatecbr";  Description: ".cbr  (comic book RAR archive)";  GroupDescription: "Associate comic file types with {#MyAppName}:"; Flags: unchecked
Name: "associatecb7";  Description: ".cb7  (comic book 7z archive)";   GroupDescription: "Associate comic file types with {#MyAppName}:"; Flags: unchecked
Name: "associatecbt";  Description: ".cbt  (comic book TAR archive)";  GroupDescription: "Associate comic file types with {#MyAppName}:"; Flags: unchecked
Name: "associatecbw";  Description: ".cbw  (WebComic)";                GroupDescription: "Associate comic file types with {#MyAppName}:"; Flags: unchecked
Name: "associatedjvu"; Description: ".djvu  (DjVu documents)";         GroupDescription: "Associate comic file types with {#MyAppName}:"; Flags: unchecked

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
Name: "{group}\{cm:ProgramOnTheWeb,{#MyAppName}}"; Filename: "{#MyAppURL}";     Components: start_menu
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"; Components: start_menu
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}";          Components: desktop
; Per-machine install (PrivilegesRequired=admin above), so this goes to the common (All Users)
; Startup folder rather than {userstartup} - matches every other per-machine artifact here
; (Program Files, HKLM App Paths) instead of tying the shortcut to whichever account ran Setup.
Name: "{commonstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Flags: runminimized; Tasks: launchatstartup

[Run]
; One hidden headless CLI invocation per selected association task - Program.cs intercepts
; --register-file-associations <ext> before Avalonia ever starts a window (not a second copy of
; the app), and clamps <ext> to the comic-format allow-list. See the file-header note on
; deviation #2 for why this shells out instead of the installer writing ProgID keys itself.
Filename: "{app}\{#MyAppExeName}"; Parameters: "--register-file-associations .pdf";  Tasks: associatepdf;  Flags: runhidden waituntilterminated; StatusMsg: "Registering .pdf association..."
Filename: "{app}\{#MyAppExeName}"; Parameters: "--register-file-associations .cbz";  Tasks: associatecbz;  Flags: runhidden waituntilterminated; StatusMsg: "Registering .cbz association..."
Filename: "{app}\{#MyAppExeName}"; Parameters: "--register-file-associations .cbr";  Tasks: associatecbr;  Flags: runhidden waituntilterminated; StatusMsg: "Registering .cbr association..."
Filename: "{app}\{#MyAppExeName}"; Parameters: "--register-file-associations .cb7";  Tasks: associatecb7;  Flags: runhidden waituntilterminated; StatusMsg: "Registering .cb7 association..."
Filename: "{app}\{#MyAppExeName}"; Parameters: "--register-file-associations .cbt";  Tasks: associatecbt;  Flags: runhidden waituntilterminated; StatusMsg: "Registering .cbt association..."
Filename: "{app}\{#MyAppExeName}"; Parameters: "--register-file-associations .cbw";  Tasks: associatecbw;  Flags: runhidden waituntilterminated; StatusMsg: "Registering .cbw association..."
Filename: "{app}\{#MyAppExeName}"; Parameters: "--register-file-associations .djvu"; Tasks: associatedjvu; Flags: runhidden waituntilterminated; StatusMsg: "Registering .djvu association..."
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Mirror of the [Run] association entries, run before files are removed (Inno's UninstallRun
; ordering) so the exe still exists to call. Each "Tasks:" gate means the line only runs for
; installs that actually opted that format in - Inno remembers the original per-task selection
; across the uninstall, no [Code] needed.
Filename: "{app}\{#MyAppExeName}"; Parameters: "--unregister-file-associations .pdf";  Tasks: associatepdf;  Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "UnregisterAssocPdf"
Filename: "{app}\{#MyAppExeName}"; Parameters: "--unregister-file-associations .cbz";  Tasks: associatecbz;  Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "UnregisterAssocCbz"
Filename: "{app}\{#MyAppExeName}"; Parameters: "--unregister-file-associations .cbr";  Tasks: associatecbr;  Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "UnregisterAssocCbr"
Filename: "{app}\{#MyAppExeName}"; Parameters: "--unregister-file-associations .cb7";  Tasks: associatecb7;  Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "UnregisterAssocCb7"
Filename: "{app}\{#MyAppExeName}"; Parameters: "--unregister-file-associations .cbt";  Tasks: associatecbt;  Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "UnregisterAssocCbt"
Filename: "{app}\{#MyAppExeName}"; Parameters: "--unregister-file-associations .cbw";  Tasks: associatecbw;  Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "UnregisterAssocCbw"
Filename: "{app}\{#MyAppExeName}"; Parameters: "--unregister-file-associations .djvu"; Tasks: associatedjvu; Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "UnregisterAssocDjvu"

[Code]

const
  // Inno writes its own uninstall entry here - "{AppId}_is1", AppId being the [Setup] value above.
  // Hardcoded rather than built from {#SetupSetting("AppId")} because AppId's value carries the
  // "{{" literal-brace escape, which mangles a preprocessed registry path. Keep this GUID in sync
  // with [Setup] AppId (it never changes - see that key's own "generated once" comment).
  PrevInstallRegKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{E6F64297-BD9E-4341-A156-5C65C2E6C1DB}_is1';

function GetPrevRegString(const ValueName: String): String;
var
  S: String;
begin
  Result := '';
  if RegQueryStringValue(HKLM, PrevInstallRegKey, ValueName, S) then
    Result := S
  else if RegQueryStringValue(HKCU, PrevInstallRegKey, ValueName, S) then
    Result := S;
end;

// Existing-install gate (deviation #3b in the file header; 2026-09-09 installer redesign,
// decision 7). Modelled on how the Visual C++ Redistributable installers react when re-run over
// an existing install: not a second full wizard, but Repair / Uninstall (same version) or a
// lighthearted upgrade prompt (older version). A fresh machine is completely unaffected - it
// returns True immediately and goes straight to Welcome.
function InitializeSetup(): Boolean;
var
  UninstallString: String;
  OldVersion: String;
  ResultCode: Integer;
  Choice: Integer;
begin
  Result := True;

  UninstallString := GetPrevRegString('UninstallString');
  if UninstallString = '' then
    exit;

  // A silent re-run (NetSparkle auto-update relaunches this installer) must never block on a
  // prompt nobody can see - fall straight through to the normal reinstall-over-existing flow.
  if WizardSilent() then
    exit;

  OldVersion := GetPrevRegString('DisplayVersion');

  if OldVersion = '{#MyAppVersion}' then
  begin
    // Re-running THIS release's installer over the same version.
    Choice := MsgBox('{#MyAppName} {#MyAppVersion} is already bunked in on this computer.' + #13#10 + #13#10 +
      'Repair it (reinstall over the existing files), or remove it?' + #13#10 + #13#10 +
      'Yes  =  Repair' + #13#10 +
      'No  =  Uninstall' + #13#10 +
      'Cancel  =  do nothing',
      mbConfirmation, MB_YESNOCANCEL);
    if Choice = IDYES then
      exit  // Repair: fall through to the normal wizard; UsePreviousAppDir pre-fills the path.
    else if Choice = IDNO then
    begin
      // Run the EXISTING install's own uninstaller (including whatever [Code] prompts that
      // version shipped, e.g. the "delete my library data" opt-in below), then bow out.
      Exec(RemoveQuotes(UninstallString), '', '', SW_SHOW, ewWaitUntilTerminated, ResultCode);
      Result := False;
    end
    else
      Result := False;  // Cancel.
  end
  else
  begin
    // Older release found - a beta point release, or a pre-2026-09-01 alpha (its DisplayVersion,
    // e.g. "0.1.1-alpha", reads back verbatim, so this one message covers every era). Tone
    // matches WelcomeLabel1/2 rather than a generic system "previous version found" dialog.
    if OldVersion = '' then
      OldVersion := 'an earlier version';
    if MsgBox('Oh hey, welcome back! Looks like you already have {#MyAppName} ' + OldVersion +
      ' bunked in here.' + #13#10 + #13#10 +
      'Ready to move up to {#MyAppName} {#MyAppVersion}? Click Yes to upgrade, or No to leave things as they are.',
      mbConfirmation, MB_YESNO) = IDYES then
      exit  // Upgrade: fall through to the normal wizard over the existing directory.
    else
      Result := False;
  end;
end;

// Opt-in "delete my library data" prompt on uninstall (deviation #3 in the file header). Paperbunkr
// itself never writes anything outside %APPDATA%\Paperbunkr (see
// src/Paperbunkr.Data/PaperbunkrDbContext.cs's own DbPath resolution, which this path matches
// exactly) - the user's actual comic/book files on disk are never touched either way, only
// Paperbunkr's own library index/settings/cache. Guarded by "not UninstallSilent" so a silent
// uninstall (e.g. driven by another installer or a scripted CI cleanup) never blocks on a prompt
// nobody can see.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    if not UninstallSilent then
    begin
      DataDir := ExpandConstant('{userappdata}\Paperbunkr');
      if DirExists(DataDir) then
      begin
        if MsgBox('Also delete your Paperbunkr library data (database, settings, and cached thumbnails)?' + #13#10 + #13#10 + DataDir + #13#10 + #13#10 + 'Your comic and book files themselves are never touched - only Paperbunkr''s own library index.', mbConfirmation, MB_YESNO) = IDYES then
          DelTree(DataDir, True, True, True);
      end;
    end;
  end;
end;
