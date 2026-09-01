# Auto-Update and Changelog

*Date: 2026-09-01.*

## Problem

PaperBunkr has no release pipeline and no update mechanism. Distribution today is a manually-run
Inno Setup installer (`installer/Installer.iss` + `installer/BuildInstaller.ps1`), built ad hoc, with
no versioned GitHub Releases, no CI, and no changelog. Getting a new build to anyone means walking
them through a manual reinstall.

CE precedent (`_reference/ComicRackCE/cYo.Common/Net/GithubAPI.cs`,
`_reference/ComicRackCE/ComicRack/MainForm.cs:4507-4554`) is an update **check**, not a self-updater:
on startup it compares the local commit against a fixed `nightly` tag via GitHub's compare API, and
if behind, shows a dialog whose "Download" button just opens the browser to the release page — no
in-app download, no file-swap, no restart, skippable via a persisted "never check on startup" flag.
CE has **no changelog UI at all**; that part is new territory for PaperBunkr, not parity.

This spec adds a real release pipeline (GitHub Actions + Velopack), an in-app updater that downloads
and applies updates itself (going further than CE's browser-handoff), and a hand-authored changelog
surfaced in-app. Decided with the user across five rounds of questions (see below); the two most
consequential calls were (1) full Velopack auto-update over a CE-style "open the browser" check, and
(2) accepting that Velopack retires the existing Inno Setup installer rather than living alongside it.

## Scope

**In scope:**

1. `.github/workflows/release.yml` — tag-triggered build, pack, and publish to GitHub Releases via
   Velopack's `vpk` CLI.
2. `<Version>` as the single source of truth in `Paperbunkr.App.csproj`, starting at `0.2.0.0`.
3. `VelopackApp.Build().Run()` wired into `Program.Main`; a new `UpdateService` wrapping
   `UpdateManager` + `GithubSource` for check/download/apply.
4. Update-available dialog (ask-before-download, CE-style prompt shape) + "update ready, restart to
   apply" toast, both reachable from a new About dialog with a manual "Check for Updates" button.
5. `CHANGELOG.md` (Keep a Changelog format) as the changelog source of truth, extracted into GitHub
   Release notes by CI, and rendered in-app in the About dialog.
6. Retiring `installer/Installer.iss` and `installer/BuildInstaller.ps1`.
7. A persisted opt-out (`AppSettings.CheckForUpdatesOnStartup`, default true).

**Explicitly out of scope:**

- Update channels (alpha/beta split) — single stream, per user direction ("moving to beta... let's
  make it beta from now on"). Only the version string carries the `-beta` suffix; releases are not
  marked GitHub pre-release.
- Auto-migrating existing Program-Files Alpha installs to the new per-user Velopack layout — see
  Migration below.
- Re-adding the installer's `HKLM ... App Paths` registry entry under the new per-user install.
  Minor loss (Run-dialog/shell name resolution by exe name only), not reinstated here.
- Delta/differential update mechanics beyond what `vpk pack` does automatically — not something this
  app needs to configure by hand.
- Any change to `FileAssociationService`/`ShellRegister` — both already write to `HKEY_CURRENT_USER`
  (`ShellRegister.cs:81`), so the install-location change doesn't touch them.

## Versioning

`Paperbunkr.App.csproj` gets `<Version>0.2.0.0</Version>` — the traditional four-part .NET assembly
version, matching how the csproj already expresses other version-shaped values. Velopack/semver
require a three-part version with an optional prerelease suffix, so CI derives the release version
by dropping the trailing revision segment and appending `-beta`:

```
0.2.0.0  →  0.2.0-beta
```

The git tag is `v0.2.0-beta` (same derived string, `v`-prefixed). CI reads `<Version>` from the
csproj, applies this derivation, and fails the build if the result doesn't match the pushed tag —
this is the drift guard, not a separate manually-maintained version anywhere else.

Bumping to the next release means: edit `<Version>` in the csproj, add the matching `## [x.y.z-beta]`
section to `CHANGELOG.md`, commit, tag, push the tag.

## Release pipeline (CI)

New `.github/workflows/release.yml`, triggered on push of a tag matching `v*-beta`:

1. `actions/checkout@v4`
2. `actions/setup-dotnet@v4` with .NET 10
3. Read `<Version>` from `Paperbunkr.App.csproj`, derive the release version (see above), assert it
   matches `GITHUB_REF_NAME` minus the leading `v` — fail the job on mismatch.
4. Extract the current version's section from `CHANGELOG.md` (everything between its `## [x.y.z-beta]`
   heading and the next `##`) into a temp file. Fail the job if no matching heading exists — a
   release cannot ship without a changelog entry.
5. `dotnet publish -c Release -r win-x64 --self-contained -o publish` — unchanged from what
   `installer/BuildInstaller.ps1` does today; Velopack doesn't need framework-dependent mode.
6. `dotnet tool install -g vpk`
7. `vpk download github --repoUrl https://github.com/heisehis/PaperBunkr` — pulls the previous
   release so Velopack can compute a delta patch. No-ops cleanly on the very first release.
8. `vpk pack --packId Paperbunkr --packVersion <derived-version> --packDir publish --mainExe
   Paperbunkr.App.exe --icon src/Paperbunkr.App/Assets/paperbunkr.ico --releaseNotes
   <changelog-section>.md`
9. `vpk upload github --repoUrl https://github.com/heisehis/PaperBunkr --publish --tag
   v<derived-version> --releaseName "Paperbunkr <derived-version>" --token
   ${{ secrets.GITHUB_TOKEN }}`

`permissions: contents: write` on the job, matching what `GITHUB_TOKEN` needs to create the release.
No `--pre` flag (see Scope).

## In-app update flow

`Program.Main` (`Program.cs:16`) gets `VelopackApp.Build().Run()` as the literal first statement,
ahead of `DiagnosticsService.Install()` — Velopack's own docs require it run before anything else
touches the process, since it's what handles the install/update lifecycle hooks Velopack's Setup.exe
invokes.

New `Services/UpdateService.cs`:

```csharp
public class UpdateService
{
    private readonly UpdateManager _manager =
        new(new GithubSource("https://github.com/heisehis/PaperBunkr", null, false));

    public bool IsInstalled => _manager.IsInstalled; // false when F5-running from the IDE
    public Task<UpdateInfo?> CheckForUpdatesAsync() => _manager.CheckForUpdatesAsync();
    public Task DownloadUpdatesAsync(UpdateInfo info, Action<int>? onProgress = null) =>
        _manager.DownloadUpdatesAsync(info, onProgress);
    public void ApplyUpdatesAndRestart(UpdateInfo info) => _manager.ApplyUpdatesAndRestart(info);
}
```

`IsInstalled` guards every call site — running via `dotnet run`/the IDE is not a Velopack-managed
install, and `UpdateManager` throws if used outside one. All update UI (menu entry, startup check,
About dialog button) no-ops or hides itself when `IsInstalled` is false.

**Startup check** — after the main window is shown, a fire-and-forget background task calls
`CheckForUpdatesAsync()` if `AppSettings.CheckForUpdatesOnStartup` is true and `IsInstalled`. On a
result: show the existing dialog-overlay pattern (`DialogHost.Avalonia`, already a dependency) with
the new version number, the changelog excerpt for it, and three actions — Download, Not now, and a
"Don't check for updates on startup" checkbox that persists to `AppSettings` — matching CE's own
dialog shape (`MainForm.cs:4529-4544`) including the persisted opt-out.

**Download** — `DownloadUpdatesAsync` runs with a progress toast. The existing `ToastProgressView`/
`ToastProgressViewModel` (`ToastProgressViewModel.cs`) covers the determinate-progress case
(`Done`/`Total`) but has no action button; this needs a small new `UpdateReadyToastViewModel`
(title + a Restart-now / Later action, no progress bar) rather than stretching the existing one to
do something it wasn't shaped for. On completion, the download toast is replaced by this one:
"Update ready — Restart to apply", with "What's New" opening the About dialog scrolled to the new
version. `ApplyUpdatesAndRestart()` fires only on explicit Restart-now — never automatically, so an
update never interrupts an in-progress reading session.

**Manual check** — a "Check for Updates" button in the About dialog runs the same
`CheckForUpdatesAsync` path, but always shows a result (including "You're up to date") rather than
only surfacing on a hit, matching CE's `alwaysCheck` branch (`MainForm.cs:4522-4526`).

## Changelog

`CHANGELOG.md` at repo root, [Keep a Changelog](https://keepachangelog.com) format:

```markdown
## [0.2.0-beta] - 2026-09-01
### Added
- Auto-update and in-app changelog.
### Fixed
- ...
```

Hand-maintained — CI step 4 above fails the release if the tag's version has no matching section,
so it's structurally impossible to ship without one.

The app copies `CHANGELOG.md` into the publish output (`<None Include="../../CHANGELOG.md"
CopyToOutputDirectory="PreserveNewest" />` on the csproj) so the About dialog renders the full
history offline — only the "is there a newer version" check needs a network call; reading what
changed doesn't.

**About dialog** (new) — reachable from the same menu/command surface the app's other top-level
dialogs use (exact entry point confirmed at plan time against the current menu structure). Shows:
current version, a "Check for Updates" button, and the rendered changelog — current version's
section expanded, older versions in a scrollable/collapsed list below. One changelog renderer, two
entry points (About dialog directly, and the update-ready toast's "What's New" link) — not two
implementations of changelog rendering.

## Installer retirement & migration

`installer/Installer.iss`, `installer/BuildInstaller.ps1`, and the `installer/Output/` /
`installer/publish/` gitignore entries are deleted — `vpk pack`/`vpk upload` replace their job
entirely.

Real behavior change worth naming: today's installer is **per-machine, admin-elevated**, installing
to Program Files (`PrivilegesRequired=admin`, `Installer.iss:42`). Velopack's generated Setup.exe
installs **per-user, no admin required**, to `%LocalAppData%\Paperbunkr`. Nothing else in the app
depends on the install location — `FileAssociationService`/`ShellRegister` already write to
`HKEY_CURRENT_USER` (`ShellRegister.cs:81`) regardless of where the exe itself lives. The one thing
lost is the installer's `HKLM\...\App Paths\Paperbunkr.App.exe` entry (Run-dialog/shell resolution by
exe name); not reinstated here (see Scope).

**No auto-migration for existing Alpha installs.** Anyone on the old Program-Files build is not
silently upgraded to the new per-user layout — the two installs are unrelated as far as Windows is
concerned. The first beta release's notes should tell existing testers to uninstall the old Alpha
build and run the new Setup.exe once. This only matters if there are testers beyond the primary user;
confirm at plan/ship time whether that's actually a live concern.

`docs/alpha-todo.md` and/or `docs/alpha-roadmap.md` get a short note (with commit ref) that
installer packaging moved from Inno Setup to Velopack, per the roadmap doc's own "update by hand"
rule for anything roadmap-relevant.

## Testing

Unit-testable pieces get normal `Paperbunkr.App.Tests` coverage: the CI version-derivation logic (if
implemented as a reusable function rather than inline shell script), the changelog-section-extraction
logic, `UpdateService`'s `IsInstalled` guard behavior, and the persisted opt-out setting round-trip.

The actual download/apply/restart cycle, and the CI pipeline itself, can only be verified by cutting
a real tagged release once this ships. That's not something a local test suite can cover — it's a
manual verification step (push `v0.2.0-beta`, confirm the GitHub Release appears with the right
notes and asset, confirm a previous-version build offers and successfully applies the update) called
out explicitly in the implementation plan rather than treated as covered by unit tests.
