; Inno Setup script for Paperbunkr (P7, docs/paperbunkr-todo.md).
;
; Packaging approach follows ComicRackCE's own precedent (_reference/ComicRackCE/Installer.iss),
; but with deliberate deviations, decided with the user before writing this file (or added in the
; 2026-09-01 full-customization pass, noted per section below):
;   1. Self-contained publish (dotnet publish -r win-x64 --self-contained) instead of CE's
;      detect-and-download-.NET-Framework-4.8 dance - Paperbunkr bundles its own runtime,
;      so no separate runtime prerequisite installer is needed.
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
; publish\License.txt - see LicenseFile below) then
; invokes this script - see that file for the expected /DMyAppVersion and /DMyAppSetupFile
; parameters.

#define MyAppName "Paperbunkr"
#ifndef MyAppVersion
#error Supply /DMyAppVersion via BuildInstaller.ps1 (the app project is the version source).
#endif
; Pure x.y.z.w form for VersionInfoVersion (the setup exe's own file-version resource) - the
; "-beta" suffix in MyAppVersion is not a valid VS_FIXEDFILEINFO version. BuildInstaller.ps1
; derives this from the -Version it's given (leading numeric run, padded to four parts) and passes
; it as /DMyAppVersionNumeric, so it tracks src/Paperbunkr.App/Paperbunkr.App.csproj's <Version>
; automatically. Direct compilation requires the same explicit version inputs.
#ifndef MyAppVersionNumeric
#error Supply /DMyAppVersionNumeric via BuildInstaller.ps1.
#endif
#if Copy(MyAppVersion, 1, Pos("-beta", MyAppVersion) - 1) != RemoveFileExt(MyAppVersionNumeric)
#error MyAppVersion and MyAppVersionNumeric must identify the same beta release.
#endif
#ifndef MyAppSetupFile
#define MyAppSetupFile "PaperbunkrSetup"
#endif
#define MyAppPublisher "Paperbunkr Project"
#define MyAppExeName "Paperbunkr.App.exe"
#define MyAppURL "https://github.com/heisehis/PaperBunkr"
#ifndef PublishDir
#define PublishDir "publish\win-x64"
#endif
#ifndef LicensePath
#define LicensePath "publish\License.txt"
#endif
#define HasThumbnailHandler FileExists(AddBackslash(SourcePath) + PublishDir + "\Paperbunkr.ShellThumbnails.dll")

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
; Show the destination page on the full flow. Quick Update skips it in ShouldSkipPage.
; Inno's default (DisableDirPage=auto) hides
; it whenever UsePreviousAppDir finds a prior install in the registry - which, with this project's
; frequent re-installs (and the InitializeSetup upgrade path), meant it almost never appeared. The
; page still pre-fills the previous directory, so a repair/upgrade is just one extra Next click.
DisableDirPage=no
; Per-machine install (Program Files, admin/UAC required) - matches CE's own PrivilegesRequired,
; chosen over a per-user install even though Paperbunkr's own file-association writes don't need
; elevation (see the file-header note above).
PrivilegesRequired=admin
OutputDir=Output
OutputBaseFilename={#MyAppSetupFile}
; Keep diagnostics for silent version rejections as well as interactive failures.
SetupLogging=yes
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
; the current emblem and wordmark from the app's own logo (src/Paperbunkr.App/Assets/paperbunkr-logo-source.png),
; per user direction to give the installer some personality rather than Inno's stock gray gradient.
; See docs/superpowers/specs/2026-09-09-installer-redesign-design.md decision 1 for the full
; rationale; the short version:
;   - Inno 6.7.3 takes PNG (with alpha) directly for both image slots - no BMP step.
;   - One genuinely-transparent PNG per slot composites correctly on both the light and dark
;     panel backgrounds, so the light and *DynamicDark directives point at the SAME two files.
;   - WizardImage.png is the full emblem+wordmark lockup; WizardSmallImage.png is the bare emblem
;     (the wordmark is illegible at the 58-159px corner-badge size).
;   - WizardImage.png is pre-padded to the modern large-image panel aspect (~164:314) with the
;     logo in the upper area and transparency below, so Inno's default stretch scales the whole
;     canvas uniformly - no distortion, and the logo lands where a wizard side image expects it.
;     (An earlier pass set WizardImageStretch=no to avoid stretching a ~square image; that clips
;     rather than fits - and it also silently clipped the 300px small badge down to a blank
;     centre slice - so it's gone. Verified on-screen 2026-09-09.)
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
LicenseFile={#LicensePath}
; No pre-install "what's new" / changelog page: release notes now live on the app's own first-run
; Welcome screen instead (user direction, 2026-09-09), so the installer no longer duplicates them.
; No InfoAfterFile page either - a separate "Information" page with its own Next button before the
; real Finish page was redundant (user direction, 2026-09-09). The quick-start pointer and the
; wiki link now live on the Finished page itself: custom FinishedLabel copy (below) + two
; postinstall [Run] checkboxes ("Open Paperbunkr now" / "Browse the wiki").
; Explicit (rather than relying on Inno's default) so the Repair / version-upgrade paths in the
; [Code] InitializeSetup gate below land back in the previously-used install directory - see
; design decision 7 ("falling through to the normal wizard").
UsePreviousAppDir=yes
UsePreviousTasks=yes
UsePreviousSetupType=yes
UsePreviousGroup=yes
UsePreviousLanguage=yes
; Auto-update (src/Paperbunkr.App/Services/UpdateService.cs) runs this same installer over a copy
; of Paperbunkr that may still be mid-shutdown when Setup starts (the app launches Setup first, so
; the UAC prompt gets the foreground, then exits; InitializeSetup waits for it below). Restart
; Manager integration below detects the app still holding {app}'s files open and closes it, instead
; of failing the file copy - the [Run] "launch after install" entry on the Finished page handles
; getting it running again, so RestartApplications is off to avoid a double-launch.
CloseApplications=yes
RestartApplications=no
; Presence check for a running Paperbunkr. Setup and the uninstaller call OpenMutex on each name
; in this comma list at startup; if any exists they show a "please close Paperbunkr" retry prompt
; before touching any files. Both the plain and Global\ names are listed because Inno does NOT
; auto-check the Global\ variant - and the app creates whichever it can (Program.cs tries Global\
; first, falls back to the session-local name). This is paced ahead of CloseApplications' Restart
; Manager pass (the fallback if the user leaves it running) - a clean, user-initiated shutdown
; matters here because Paperbunkr's library DB is a single shared SQLite file. The mutex names
; MUST stay identical to Program.cs's RunningMutexName.
AppMutex=Paperbunkr_App_Running,Global\Paperbunkr_App_Running

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
; Custom Welcome-page copy (user direction: "give the installer personality") - Inno Setup's
; %n is a literal line break within a [Messages] string, not a placeholder needing escaping.
WelcomeLabel1=Welcome to {#MyAppName}
WelcomeLabel2=A home for your next great read.%n%nInstall {#MyAppName} {#MyAppVersion} to organise and read comics, manga and books.%n%nThe .NET runtime is included.%n%nYour library data is stored locally. Online features can contact their services.%n%nThis is beta software. Please report problems through our GitHub project.%n%nSelect Next to get started.
WizardSelectComponents=Choose shortcuts
SelectComponentsDesc=The complete application is always installed. These choices only change its shortcuts.
SelectComponentsLabel2=Select the shortcuts you want to create:
WizardSelectTasks=Make it yours
SelectTasksDesc=Choose optional Windows integrations.
SelectTasksLabel2=File associations and cover thumbnails are optional. You can also manage them later in Preferences.
; Custom Finished-page copy, matching the Welcome page's personality (user direction). The Finished
; page in "modern" style already mirrors the Welcome page - same big side image, its own Finish
; button - so this is where the post-install info lives now (no separate InfoAfterFile page). The
; two postinstall [Run] checkboxes ("Open Paperbunkr now" / "Browse the wiki") render just below
; this text.
FinishedHeadingLabel=Your next read is ready.
FinishedLabel={#MyAppName} {#MyAppVersion} has been installed on this computer.%n%nNew here? Add your first comic or book folder from Preferences > Library. The wiki has the full walkthrough.

[Types]
Name: "full";    Description: "Start Menu and desktop shortcuts";
Name: "compact"; Description: "Start Menu shortcut only";
Name: "custom";  Description: "Choose shortcuts"; Flags: iscustom

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
; Books (Novels) per-format tasks (docs/superpowers/specs/2026-09-16-book-file-associations-
; design.md), same opt-in-off-by-default shape as the comic tasks above. ".pdf" stays comic-only
; (no separate Book PDF task - see FileAssociationService.BookFormats' own doc comment on why a
; second row for the same extension would fight the comic one). "FB2" covers both ".fb2" and bare
; ".zip" (".fb2.zip" is a common FB2 distribution convention, and Windows has no way to key an
; association off a compound extension) - checking this box means Paperbunkr becomes the default
; handler for plain .zip files too, not just .fb2.zip ones; the checkbox description says so.
Name: "associateepub"; Description: ".epub  (EPUB e-books)";                          GroupDescription: "Associate Book file types with {#MyAppName}:"; Flags: unchecked
Name: "associatefb2";  Description: ".fb2, .zip  (FictionBook 2 - also claims plain .zip)"; GroupDescription: "Associate Book file types with {#MyAppName}:"; Flags: unchecked
Name: "associatemobi"; Description: ".mobi, .azw, .azw3  (Kindle / MOBI)";            GroupDescription: "Associate Book file types with {#MyAppName}:"; Flags: unchecked
; File Explorer cover thumbnails (docs/superpowers/specs/2026-09-30-explorer-cover-thumbnails-design.md,
; decisions 13/17): one opt-in box; turns on every thumbnail type no other program already draws.
#if HasThumbnailHandler
Name: "explorerthumbnails"; Description: "Show comic and book covers as thumbnails in File Explorer"; GroupDescription: "File Explorer:"; Flags: unchecked
#endif

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
Filename: "{app}\{#MyAppExeName}"; Parameters: "--register-file-associations .epub";     Tasks: associateepub; Flags: runhidden waituntilterminated; StatusMsg: "Registering .epub association..."
Filename: "{app}\{#MyAppExeName}"; Parameters: "--register-file-associations .fb2 .zip"; Tasks: associatefb2;  Flags: runhidden waituntilterminated; StatusMsg: "Registering .fb2/.zip association..."
Filename: "{app}\{#MyAppExeName}"; Parameters: "--register-file-associations .mobi .azw .azw3"; Tasks: associatemobi; Flags: runhidden waituntilterminated; StatusMsg: "Registering .mobi/.azw/.azw3 association..."
; After the associations, so the handler is also registered under any Paperbunkr ProgID they just created.
#if HasThumbnailHandler
Filename: "{app}\{#MyAppExeName}"; Parameters: "--register-thumbnails"; Tasks: explorerthumbnails; Flags: runhidden waituntilterminated; StatusMsg: "Registering File Explorer thumbnails..."
#endif
; Finished-page checkboxes (postinstall = rendered as a checkbox on the Finished page;
; skipifsilent = hidden for silent/auto-update runs). "Open Paperbunkr now" stays checked by
; default; "Browse the wiki" is opt-in (unchecked). The wiki entry is a URL opened via the shell
; (shellexec) as the logged-in user rather than the elevated installer account (runasoriginaluser).
Filename: "{app}\{#MyAppExeName}"; Description: "Open {#MyAppName} now"; Flags: nowait postinstall skipifsilent
Filename: "{#MyAppURL}/wiki"; Description: "Browse the {#MyAppName} wiki"; Flags: postinstall shellexec runasoriginaluser skipifsilent unchecked

[UninstallRun]
; Unregister ALL comic + Book file associations on uninstall, unconditionally - not gated on the
; original per-format [Tasks] selection. Bare "--unregister-file-associations" (no extension
; args) clears the whole FileAssociationService.ComicAssociationExtensions +
; BookAssociationExtensions set; each per-format unregister is idempotent, so this is a no-op for
; formats that were never associated. Doing it unconditionally avoids orphaned ProgID keys when
; task state drifts across an upgrade/repair (e.g. a format associated by an older build whose
; task the user later unchecked). Runs before files are removed (Inno's UninstallRun ordering) so
; the exe still exists to call.
; Thumbnails first, while Paperbunkr's ProgIDs still exist, so each type's previous thumbnail handler is put back
; where it was (decision 7). Unconditional for the same reason as associations; a no-op if never enabled.
Filename: "{app}\{#MyAppExeName}"; Parameters: "--unregister-thumbnails"; Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "UnregisterThumbnails"
Filename: "{app}\{#MyAppExeName}"; Parameters: "--unregister-file-associations"; Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "UnregisterFileAssociations"

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
// labelled update choices (older version). A fresh machine is completely unaffected - it
// returns True immediately and goes straight to Welcome.
var
  QuickUpdate: Boolean;
  DeleteProfile: Boolean;
  ProfileToDelete: String;

#include "InstallerLogic.iss"

function RejectSetup(const MessageText: String): Boolean;
begin
  Log(MessageText);
  if not WizardSilent() then
    TaskDialogMsgBox('Setup cannot continue', MessageText, mbError, MB_OK, ['Close'], 0);
  // InitializeSetup=False gives Inno's documented nonzero initialization exit status.
  Result := False;
end;

function InitializeSetup(): Boolean;
var
  UninstallString, OldVersion, InstalledDir, FileVersion: String;
  ResultCode, Choice, Waited, Comparison: Integer;
begin
  Result := True;
  QuickUpdate := False;
  UninstallString := GetPrevRegString('UninstallString');
  if UninstallString <> '' then
  begin
    OldVersion := GetPrevRegString('DisplayVersion');
    if not TryCompareReleases(OldVersion, '{#MyAppVersion}', Comparison) then
    begin
      InstalledDir := GetPrevRegString('InstallLocation');
      if (InstalledDir = '') or not GetVersionNumbersString(
        AddBackslash(InstalledDir) + '{#MyAppExeName}', FileVersion) then
        FileVersion := '';
      if not TryCompareReleases(FileVersion, '{#MyAppVersion}', Comparison) then
      begin
        Result := RejectSetup('The installed Paperbunkr version could not be read. ' +
          'Setup has left it unchanged. Check its installation or use its existing uninstaller before trying again.');
        exit;
      end;
      OldVersion := FileVersion;
    end;
    if Comparison > 0 then
    begin
      Result := RejectSetup('Paperbunkr ' + OldVersion + ' is already installed. This installer contains {#MyAppVersion}.' +
        ''#13#10#13#10 + 'Installing an older release is blocked because it may not support your library database. ' +
        'Use an installer for the same or a newer release.');
      exit;
    end;
    // The safety gate precedes this silent shortcut, so silent downgrades are blocked too.
    QuickUpdate := Comparison < 0;
    if not WizardSilent() then
    begin
      if Comparison = 0 then
      begin
        Choice := TaskDialogMsgBox('Paperbunkr is already installed',
          'Installed release: ' + OldVersion + #13#10 + 'Installer: {#MyAppVersion}',
          mbConfirmation, MB_YESNOCANCEL, ['&Repair'#13#10'Reinstall application files and review installation options.',
           '&Uninstall'#13#10'Remove the application using its existing uninstaller.', '&Cancel'], 0);
        if Choice <> IDYES then
        begin
          if Choice = IDNO then
          begin
            if not Exec(RemoveQuotes(UninstallString), '', '', SW_SHOW, ewNoWait, ResultCode) then
              TaskDialogMsgBox('The uninstaller could not start', SysErrorMessage(ResultCode) +
                ''#13#10 + 'You can try again from Windows Installed apps.', mbError, MB_OK, ['Close'], 0);
          end;
          Result := False;
          exit;
        end;
      end
      else
      begin
        Choice := TaskDialogMsgBox('Update Paperbunkr',
          'Installed: ' + OldVersion + #13#10 + 'Available: {#MyAppVersion}' + #13#10#13#10 +
          'Your library data will be preserved.', mbConfirmation, MB_YESNOCANCEL, ['&Update'#13#10'Keep the current location, shortcuts and integration choices.',
           '&Change installation options'#13#10'Review the location, shortcuts and integrations.', '&Cancel'], 0);
        if (Choice <> IDYES) and (Choice <> IDNO) then begin Result := False; exit; end;
        QuickUpdate := Choice = IDYES;
      end;
    end;
  end;

  // In-app update starts Setup before shutdown. Give its backup/exit handlers up to 60s.
  if ExpandConstant('{param:PAPERBUNKRUPDATE|0}') = '1' then
  begin
    Waited := 0;
    while CheckForMutexes('{#SetupSetting("AppMutex")}') and (Waited < 60000) do
    begin
      Sleep(250);
      Waited := Waited + 250;
    end;
  end;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := SkipQuickUpdatePage(QuickUpdate, PageID);
end;

procedure InitializeWizard();
begin
  if QuickUpdate then
  begin
    WizardForm.FinishedHeadingLabel.Caption := 'Paperbunkr has been updated.';
    WizardForm.FinishedLabel.Caption := 'Paperbunkr {#MyAppVersion} has been installed.' +
      ''#13#10#13#10 + 'Your library is ready when you are.';
  end;
end;

// Choose before application removal; perform deletion only after CLI cleanup has finished.
function InitializeUninstall(): Boolean;
var
  Choice: Integer;
begin
  Result := True;
  DeleteProfile := False;
  ProfileToDelete := ExpandConstant('{userappdata}\Paperbunkr');
  if UninstallSilent() or not DirExists(ProfileToDelete) then exit;
  Choice := TaskDialogMsgBox('Keep your Paperbunkr library data?',
    'Removing the application can leave your library ready for a future installation.' + #13#10#13#10 +
    'Profile folder: ' + ProfileToDelete + #13#10#13#10 +
    'Custom data locations and external databases are preserved. Comic and book folders are not removed.',
    mbConfirmation, MB_YESNO, ['&Keep library data'#13#10'Recommended. Preserve this profile and remove only the application.',
     '&Delete library data'#13#10'Also remove the contents of the profile folder.'], 0);
  if Choice <> IDNO then exit;
  if not IsSafeProfileTarget(ProfileToDelete, ExpandConstant('{userappdata}')) then
  begin
    TaskDialogMsgBox('Library data will be kept',
      'The profile contains a redirected or unreadable location. Setup will remove the application only.',
      mbInformation, MB_OK, ['Continue'], 0);
    exit;
  end;
  DeleteProfile := MsgBox('Permanently delete everything in this Paperbunkr profile folder?' +
    ''#13#10#13#10 + ProfileToDelete + #13#10#13#10 +
    'This includes the database, settings, cached covers/thumbnails, backups, plugins and logs.' +
    ''#13#10#13#10 + 'Comic and book library folders and custom data locations will remain. ' +
    'Choose No to keep the profile and continue removing the application.',
    mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and DeleteProfile then
  begin
    if not IsSafeProfileTarget(ProfileToDelete, ExpandConstant('{userappdata}')) or
      not RemovePlainTree(ProfileToDelete) then
    begin
      Log('Profile deletion failed or was refused: ' + ProfileToDelete);
      MsgBox('The application was removed, but some library data could not be deleted.' +
        ''#13#10#13#10 + 'The remaining folder is: ' + ProfileToDelete,
        mbInformation, MB_OK);
    end;
  end;
end;
