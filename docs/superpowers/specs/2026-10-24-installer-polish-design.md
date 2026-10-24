# Installer polish

Date: 2026-10-24
Status: Design approved in chat; user instructed implementation after the written spec was created.

## Purpose and decisions

Polish the complete Windows installer experience: branding, wording, installation choices,
repair, upgrade, downgrade protection, and uninstall. The user approved a full pass using
Inno Setup, restrained personality, labelled maintenance actions, shorter upgrades with access
to options, blocking downgrades, and preserving user data by default with confirmed deletion.
The complete design was presented and confirmed before this document was written.

Three approaches were considered: copy/artwork only; refinement of the existing Inno wizard
(chosen); and a custom installer shell. The first leaves maintenance problems unresolved;
the third adds a second UI implementation without a corresponding user benefit.

## Evidence and existing contracts

- `installer/Installer.iss` uses `modern dynamic`, branded welcome/finish pages, an AGPL/terms
  acceptance page, machine-wide installation, and app CLI calls for associations/thumbnails.
- `_reference/ComicRackCE/Installer.iss` establishes the Inno, admin/Program Files, licence,
  install-type and launch-after-install precedent. CE's prerequisite installation and direct
  registry associations are deliberately superseded by Paperbunkr's bundled runtime and CLI.
- The September 9 installer redesign deliberately chose opt-in associations, an always-visible
  destination page, playful copy, native control accents, and quick-start/wiki on Finish.
  This design supersedes the copy and maintenance flow and the destination rule for the quick
  upgrade path; the full configuration path retains destination selection.
- Existing wizard PNG changes belong to the October brand update. Use those assets as the
  starting point and preserve their artwork; do not revert or regenerate the logo.
- The build script defaults to `0.3.0-beta-<hash>` and direct ISS compilation to `0.3.1-beta`,
  while the app project currently declares `0.7.88.0`. CI derives its version from the project.
- `ReleaseVersion` compares major/minor/patch, ignoring the revision, suffix and build identity.
  Follow this existing single-stream ordering rather than introducing release channels.
- `UpdateService.ApplyUpdatesAndRestart` passes `/PAPERBUNKRUPDATE=1`, without `/SILENT`.
  This is an interactive update; the flag currently enables a bounded shutdown/mutex wait.
- A local Native AOT failure permits packaging without thumbnails, but the current wizard still
  offers their checkbox. CI already treats that publish failure as fatal.

## Visual design and wording

Retain the current orange/white emblem and wordmark, dark brand panel, native light/dark wizard
controls, and the small emblem on inner pages. Adjust placement/padding only where inspection
shows clipping, distortion, excessive dead space, or poor legibility. Keep the large image at
the panel's aspect ratio; the earlier redesign established that disabling image stretch clips
oversized assets rather than fitting them. Native Inno controls remain native; do not introduce
a Delphi style dependency or Avalonia bootstrapper.

Use short, direct copy with a little personality on Welcome and Finish; maintenance and errors
use precise action language. Welcome identifies Paperbunkr as a comic, manga and book library
and reader, names the installed version, and explains that the runtime is included. Replace
the blanket claim that nothing leaves the machine: library data is stored locally, while
online features can contact their services. Avoid promises about all network activity.

Finish states success and the installed version, retains launch enabled by default and the
unchecked wiki option, and provides the existing first-folder pointer after verifying its
current in-app location. Release notes remain in the app rather than gaining an installer page.

## Installation and maintenance flows

| Situation | Behavior |
| --- | --- |
| Fresh installation | Full guided wizard: Welcome, licence/terms, destination, components/shortcuts, tasks, review, progress, Finish. |
| Same numeric release | Labelled Repair / Uninstall / Cancel choices. Repair reinstalls with previous choices through the full wizard; Uninstall starts the existing uninstaller; Cancel exits without changes. |
| Newer numeric release | Labelled Update / Change installation options / Cancel choices, showing installed and incoming versions. |
| Quick Update | Skip Welcome and destination/component/task/group configuration pages; retain licence acceptance, review, progress and Finish. Reuse the existing directory, shortcuts and task choices. |
| Change installation options | Full guided flow prefilled with prior installation choices. |
| Older numeric release | Block before installation changes, identify both versions, and explain that an older app may not support the existing library database. |
| Unreadable installed version | Try the installed app's numeric file version at the registered installation path. If still unreadable, stop with a clear diagnostic rather than guess that this is an upgrade. |

The destination and options remain accessible by choosing Change installation options; the
quick path does not require inventing another settings page. The review page reports the
destination and choices before copying files. New optional integrations remain off when no
previous selection exists. Preserve existing component/task identifiers so old choices migrate.

Silent setup applies the same version guard before copying files. It never displays the
maintenance choice dialogs; allowed repairs/upgrades preserve settings and proceed using
Inno's silent behavior. Downgrade or unreadable-version rejection is logged and exits with a
failure status, without an invisible prompt. Preserve the updater's bounded mutex wait,
signature validation and single launch-after-install behavior.

All cancellation paths leave the existing installation untouched. Report an uninstaller
launch failure instead of silently exiting as if removal had started.

## Choices and packaging truthfulness

Keep machine-wide Program Files installation and existing shortcut defaults. Full/Compact/
Custom currently change only shortcuts, not installed reader capabilities; make that clear
in the component-page wording rather than implying different application payloads.

Startup, format associations and Explorer thumbnails stay opt-in. Group and word the options
clearly. Keep the explicit warning that the FB2 association also claims plain `.zip`; this
pass does not change the supported extension list or association implementation. Registration
continues through the app's existing headless CLI, shared with Preferences.

Show the thumbnail option only if the packaged payload contains the Native AOT handler. A
local build without it must omit the task and its registration invocation; CI keeps failing
if the required handler cannot be built. Avoid stale output masquerading as a successful
handler build. Existing unconditional uninstall cleanup remains idempotent.

## Version source

Derive the default installer release version from `Paperbunkr.App.csproj` using the same
three-part plus `-beta` rule as CI. Local build identity may retain the existing git/dev suffix,
but it must use the current numeric release and must not affect ordering. Keep CI's tag check
and explicit `-Version` interface; reject malformed or numerically mismatched overrides before
publishing. Direct ISS compilation must derive coherent identity from its published app or
require explicit version inputs with an actionable compile error; remove stale literal fallbacks.

Compare numeric components, never version strings lexically: `0.7.10` is newer than `0.7.9`.
Suffix differences within the same numeric release go through Repair, not Upgrade. Refresh
obsolete runtime comments to match the actual self-contained target; no runtime strategy change.

## Uninstall and data

Preserve user data by default. Offer Keep library data / Delete library data with Keep as the
safe default. Delete requires a second explicit confirmation naming the exact directory and
the contents affected: database, settings, cached covers/thumbnails, and other contents of that
Paperbunkr profile folder, including backups/plugins/logs where present. Cancelling the deletion
choice preserves data and allows application removal to proceed. Silent uninstall preserves data.

Keep cleanup restricted to the existing default per-user roaming Paperbunkr folder. Do not
follow `PAPERBUNKR_DATA_DIR`, a database override, library paths, junctions/reparse points or
other users' profiles for recursive deletion. Display the resolved target and verify it is the
expected profile child before deleting. This pass does not broaden cleanup to LocalAppData or
external databases; disclose that custom locations remain. Report deletion failure accurately.

Comic and book library files are outside this cleanup scope and must remain untouched. Do not
claim every byte of app data was removed when custom locations or preserved folders remain.

## Implementation boundaries and verification

Primary changes belong in `installer/Installer.iss`, `installer/BuildInstaller.ps1`, and focused
installer validation helpers where useful. Preserve AppId, install identity, release pipeline,
update signatures and CLI registration ownership. No unrelated app UI or database changes.
Use native task dialogs for descriptive actions; Inno documents custom labels in
https://jrsoftware.org/ishelp/topic_isxfunc_taskdialogmsgbox.htm and restoration of previous
tasks in https://jrsoftware.org/ishelp/topic_setup_useprevioustasks.htm.

Avalonia deployment guidance was checked at
`C:/Users/DeeDee/.agents/skills/avalonia/avalonia-deployment/SKILL.md`.
Use `avalonia-pro-max/review-checklist/SKILL.md` for applicable visual/accessibility checks;
XAML-specific token requirements do not apply to native Inno controls. Load additional matching
Avalonia skills only if implementation expands into app UI.

Verification must include:

- Installer compilation with current version inputs, and with/without the thumbnail payload.
- Numeric ordering, equal numeric versions with different suffixes, malformed versions, and
  prevention of stale/mismatched version labels.
- Fresh install, repair, quick upgrade, configurable upgrade, cancellation, downgrade block,
  unknown-version block, and existing-uninstaller launch errors.
- Interactive updater flag/mutex behavior and silent repair/upgrade/downgrade outcomes.
- Retention of directory, shortcut and task selections, with new integrations off by default.
- Uninstall preservation, deletion confirmation/cancellation, silent preservation, deletion
  failure, and untouched library/custom-path sentinel files.
- Light/dark appearance at 100%, 150% and 200% display scale, complete text/artwork without
  clipping, keyboard operation, focus visibility, and meaningful screen-reader button names.

Run install/uninstall and registry-changing checks only in a disposable Windows VM or equivalent
isolated environment with fixture profiles and comic/book sentinel files. Do not install over
the user's working app or delete their real profile to validate this design. If that environment
is unavailable, complete compilation/static checks and explicitly record the remaining runtime
checks as outstanding; a successful compile alone does not prove installer UX.

After implementation, update `docs/paperbunkr-todo.md` with actual verification and commit status.
Do not mark the installer polish implemented or validated on the basis of this spec commit.
