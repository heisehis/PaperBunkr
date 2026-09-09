# Installer redesign — design

**Date:** 2026-09-09
**Status:** approved, pending write-up into an implementation plan
**Scope:** `installer/Installer.iss`, `installer/BuildInstaller.ps1`, `installer/Assets/*`,
`src/Paperbunkr.App/Program.cs` (file-association registration only), `src/Paperbunkr.App/Paperbunkr.App.csproj`
(version bump, already applied).

## Why

The app's logo was replaced (see the same-day chat: new hexagon-"P" emblem in orange/amber,
`src/Paperbunkr.App/Assets/paperbunkr-logo-source.png` + `paperbunkr.ico`). The installer's own
wizard art (`installer/Assets/WizardImage.bmp`, `WizardSmallImage.bmp`, sourced from
`welcome-source.png`) still uses the *old* teal hooded-reader mascot — now visibly mismatched with
the app it installs. Separately, the installer's copy and version defaults still say "Alpha"
(`WelcomeLabel1/2` in `Installer.iss`, the `0.1.0-alpha` fallback in both `Installer.iss` and
`BuildInstaller.ps1`) even though the app moved to beta on 2026-09-01. This installer build targets
the **next** release, `v0.3.0-beta` — the app's `<Version>` in `Paperbunkr.App.csproj` has already
been bumped from `0.2.0.0` to `0.3.0.0` as part of this work (single source of truth per the
existing comment there and `docs/superpowers/specs/2026-09-01-auto-update-and-changelog-design.md`).

Ground-truthed against Inno Setup 6.7.3 (the version actually installed — confirmed via the
Windows uninstall registry, not assumed) and its current jrsoftware.org docs, not guessed from
memory.

## Decisions

### 1. Wizard branding: `WizardStyle=modern dynamic`, one shared transparent asset

`WizardStyle=modern dynamic` — Inno checks the Windows theme at Setup launch and swaps which
wizard image directive it draws from (`dynamic` requires Inno 6.4+; we have 6.7.3). No named
built-in color palette (`polar`/`slate`/`stellar`/`windows11`/`zircon`) is used — none match the
brand's orange/amber, so the personality comes entirely from the image, not the chrome.

Both the light and dark side-image slots (`WizardImageFile` and `WizardImageFileDynamicDark`) —
and both small-badge slots (`WizardSmallImageFile` and `WizardSmallImageFileDynamicDark`) — point
at the **same two files**:

- `installer/Assets/WizardImage.png` (900×849, RGBA, genuinely transparent) — the full
  emblem+wordmark lockup. Built by flood-filling the black background off the user-provided source
  art (`PAPPERBUNKR LOGO (1).png`, which had it baked in as opaque black, no alpha channel) from
  all four canvas edges inward; the artwork's own black outline strokes survive untouched because
  they're topologically disconnected from the canvas border. One transparent asset composites
  correctly on both Setup's light and dark panel backgrounds — no separate light/dark variant
  needed (this was tried first and correctly called out as unnecessary work).
- `installer/Assets/WizardSmallImage.png` (300×300, RGBA, transparent) — the bare emblem, no
  wordmark. The corner badge renders at 58×58 (100% DPI) up to ~159×159 (250% DPI); the full
  lockup's wordmark is illegible at that size regardless of mode, so the badge stays emblem-only
  in both light and dark rather than forcing lockup-everywhere literally.

Inno 6.7.3 accepts PNG directly for both `WizardImageFile*` and `WizardSmallImageFile*` (with
transparency) — no BMP conversion step is needed, unlike the current `.bmp` assets.

~~`WizardImageStretch=no` should be set: the lockup's own aspect ratio (~1.06:1, roughly square) is
nothing like the wizard's tall/narrow default side-panel area (164:314 ≈ 0.52:1), and stretching a
near-square image to fill a tall narrow rectangle would visibly distort it. With `Stretch=no` it
centers at its natural size instead, letterboxed by the panel's own background color (which, being
transparent PNG on a themed panel, reads as intentional framing rather than an error).~~

> **Correction (2026-09-09, on-screen test):** `WizardImageStretch=no` was wrong. It *clips* (not
> letterboxes) an image larger than the panel — the 900px-wide lockup showed as a ~15px vertical
> slice through its centre — and it **also applies to the small badge**, clipping the 300px emblem
> down to a blank centre slice (user saw "a blank bar" top-right). Fix: drop the directive (back to
> the default stretch), and pre-pad `WizardImage.png` to the panel aspect (~164:314) with the logo
> in the upper area and transparency below, so the uniform stretch introduces no distortion.
> `WizardSmallImage.png` (300×300, square) needs no change once the directive is gone.

**Brand color, split by system theme** (confirmed via a second grilling round): `WizardBackColor`/
`WizardImageBackColor` accept literal `#rrggbb` hex (not just Inno's 5 named style presets), and
their `*DynamicDark` counterparts are built specifically to pair with `WizardStyle=modern dynamic`
— not a workaround. Values are the app's own real Default skin colors
(`src/Paperbunkr.App/Assets/Skins/default/theme.json`), not eyeballed:

- **System dark:** full brand chrome — `WizardBackColorDynamicDark=#0A0B0D`,
  `WizardImageBackColorDynamicDark=none` (page and image-panel backgrounds end up the same dark
  tone either way; `none` keeps the PNG's own transparency rather than duplicating the hex).
  Directly reuses the app's actual dark skin, not a guess.
- **System light:** middle ground, not full chrome — `WizardBackColor` left at Inno's own default
  (plain light page), only `WizardImageBackColor=#0A0B0D` set, so the logo panel stays dark/branded
  while the rest of the wizard (body text, buttons) stays Inno's native light appearance. Chosen
  over a full light+amber chrome because no such skin exists in the app to ground it in — the dark
  side reuses a real, already-shipped skin; a "white and amber" full chrome would be invented from
  scratch and risks untested contrast issues.

**Resolved: button/control accent colors stay Inno's default, by necessity, not by choice.** The
above only covers *background* colors (page + image panel); recoloring button/control accents
(e.g. Next/Back/checkboxes picking up `#C9803F`) needs a custom `.vsf` style file
(`WizardStyleFile`/`WizardStyleFileDynamicDark`) — and `.vsf` turns out to be a Delphi/RAD Studio
VCL Styles file, not a plain config Inno lets you hand-author. It's built with Delphi's Bitmap
Style Designer (a separate commercial IDE, not part of Inno Setup) or downloaded through
Embarcadero's GetIt package manager (which itself requires a Delphi install, even for the free
built-in styles) or a paid third-party marketplace. None of that tooling is available in this
environment, and fabricating the binary format by guesswork risks shipping a corrupt style file
rather than a working one — so this is confirmed out of reach for this pass, not merely deferred.
Buttons/controls keep Inno's own default accent color; only the backgrounds carry the brand amber.

**Superseded, to be deleted when `Installer.iss` is repointed:** `WizardImage.bmp`,
`WizardSmallImage.bmp`, `welcome-source.png` (all still present in `installer/Assets/` as of this
doc; not yet removed since `Installer.iss` hasn't been repointed).

### 2. Wizard copy: personality kept, alpha→beta facts fixed

`WelcomeLabel1`/`WelcomeLabel2` keep their existing tone and length (deliberate prior user
direction, not broken) — only the factual "Alpha" → "Beta" swap, done. `FinishedLabel` needed no
change (no alpha-specific wording in it).

Version defaults fixed to `0.3.0-beta`, done: `Installer.iss`'s `#ifndef MyAppVersion` fallback and
`BuildInstaller.ps1`'s no-`-Version`-arg fallback (`"0.3.0-beta-$shortCommit"`).

### 3. License page: `LicenseFile`, combining LICENSE + TERMS.md

The repo has two separate legal documents at root: `LICENSE` (AGPLv3, code license) and
`TERMS.md` (usage/liability terms — already surfaced in-app via Preferences → About → Legal, per
the 2026-09-08 About redesign). `TERMS.md` opens with "By downloading, installing, or running
Paperbunkr… you agree to the following," which is exactly the kind of claim that should be backed
by a real install-time accept click rather than living only in an About screen nobody has to open.

Inno's `LicenseFile` directive supports exactly one document behind its single accept-gate page —
no built-in second page without custom Pascal scripting (and a second custom license-acceptance
page specifically stays out of scope; decision 7 below is the one place this design does reach for
`[Code]`, for a different reason). `BuildInstaller.ps1` will generate a combined plain-text file at
build time (same pattern already
used for `WhatsNew.txt` from `CHANGELOG.md`: strip markdown headings/emphasis down to plain text,
since Inno's license viewer doesn't render markdown), concatenating LICENSE followed by TERMS.md,
and `Installer.iss` points `LicenseFile` at that generated file. This shows before the
install-directory step, matching ComicRackCE's own precedent (`_reference/ComicRackCE/Installer.iss`
has its own `LicenseFile`).

### 4. Post-install page: `InfoAfterFile`, quick-start + wiki link

> **Correction (2026-09-09, user direction):** the installer now has **no "Information" pages at
> all**.
> - The pre-install `InfoBeforeFile` "what's new" / changelog page (and its `WhatsNew.txt`
>   generation) is gone — release notes move to the app's own first-run Welcome screen.
> - The `InfoAfterFile` page described below was also dropped: a separate page with its own
>   **Next** button before the real Finish page was redundant. Its content moved onto the
>   **Finished page** itself (which in `modern` style already mirrors the Welcome page — same big
>   side image, its own **Finish** button): a custom `FinishedHeadingLabel`/`FinishedLabel` ("…has
>   been installed on this computer" + a one-line quick-start pointer) plus two `postinstall`
>   `[Run]` checkboxes — **"Open Paperbunkr now"** (checked) and **"Browse the wiki"** (unchecked,
>   `shellexec` the wiki URL as the logged-in user). No `InfoAfter.txt` is generated any more.
> - `DisableDirPage=no` added so the "Select Destination Location" page always shows — Inno's
>   `auto` default had been hiding it on every machine with a prior install in the registry.
> - `FinishedLabel` does not repeat "Click Finish…" — Inno prints its own `ClickFinish` line.

A new `InfoAfterFile` page (parallel to the existing `InfoBeforeFile` "what's new" page) shown
after a successful install, before Finish. Content: a short quick-start (where Preferences lives,
Preferences → Libraries to add a first folder) plus a link to the published wiki
(`https://github.com/heisehis/PaperBunkr/wiki`, confirmed from `wiki/publish-wiki.sh`). Generated
as a static file the same way `WhatsNew.txt` is (plain text, written by `BuildInstaller.ps1`) since
its content doesn't depend on build-time state the way the changelog excerpt does.

### 5. File-association tasks: split by format, comic-specific only

Ground-truthed via `Providers.Readers.GetSourceFormats()` (reflection over `[FileFormat]`
attributes in `Paperbunkr.Engine`, not a hand-maintained list) — see the grilling-session subagent
report for the full raw group list. Two real findings changed the shape of this:

- **Book formats (EPUB/FB2/MOBI) are a separate registry** (`BookTextSourceFactory`, not
  `ImageProvider`-based) — not associable today regardless of what the installer offers, so they're
  out of scope here.
- **The current single "associate" checkbox already over-associates.** `Program.cs`'s
  `--register-file-associations` loops over every group `GetAvailableFormats()` returns, which
  includes generic `ZIP Archive` (`.zip`), `RAR Archive` (`.rar`), and `7z Archive` (`.7z`) —
  meaning today's installer already hijacks bare `.zip`/`.rar`/`.7z` as Paperbunkr files, not just
  the comic-specific extensions. This is a pre-existing bug, not something this redesign
  introduces, but splitting the checkbox is the natural point to fix it.

> **Correction (2026-09-09, during implementation):** `.cbt` ("eComic (TAR)") was added back —
> **7** tasks, not 6. ComicRack CE's own installer associates `.cbz/.cbr/.cb7/.cbt/.cbw`, and the
> engine registers a real reader for `.cbt`; the rationale below only argues against generic
> `.zip/.rar/.7z`, so dropping `.cbt` was an oversight. The allow-list clamped into
> `FileAssociationService.ComicAssociationExtensions` is
> `.pdf .cbz .cbr .cb7 .cbt .cbw .djvu`.

New `[Tasks]` entries, one per comic-specific format, replacing the single `associate` task:
**PDF**, **CBZ**, **CBR** (merging the duplicate RAR/RAR5 provider registrations, which both claim
`.cbr`, into one checkbox), **CB7**, **CBT** (added per the correction above), **WebComic**
(`.cbw`), **DjVu**. The generic
ZIP/RAR/7z-archive entries are dropped from what the installer (and the `--register-file-associations`
CLI path) associates at all — fixed at the `Program.cs` source (skip `FileFormat` entries whose
extension isn't one of the six comic-specific ones above when the CLI flag drives association), so
the fix is scoped to the install/CLI association flow only. **Preferences → Advanced is explicitly
untouched** — it's a different screen with a different job (post-install fine-tuning, already
per-format), not part of this task, and its own generic-archive behavior (if any) is a separate
question not raised here.

All seven new tasks default unchecked, matching the current single task's default (opt-in, not
opt-out) and the file's existing rationale (Preferences → Advanced offers the same toggle
post-install for anyone who skips this).

### 6. `VersionInfo*` keys

Explicit `VersionInfoCompany`, `VersionInfoDescription`, `VersionInfoCopyright`,
`VersionInfoVersion` (tied to `{#MyAppVersion}`) added to `[Setup]` — populates the setup exe's own
Explorer "Properties → Details" tab. Cheap, no further decisions cascade from it.

### 7. Existing-install gate: Repair / Uninstall, VC++-redistributable style

The one place this design reaches for real `[Code]` Pascal scripting, added after a second grilling
round surfaced a genuine need — not something generic Pascal-page customization was speculatively
added for. **One example originally floated for this (auto-detecting a ComicRack CE install and
offering an import) turned out to be redundant and was dropped**: the app already has a full
first-run onboarding flow (`WelcomeOverlay`/`WelcomeOverlayViewModel`,
`docs/superpowers/specs/2026-08-31-first-run-onboarding-*`) that auto-detects CE at startup and
badges an "Import from ComicRack CE" card, backed by `MigrationViewModel`'s real import logic — an
installer-level duplicate would race with a system that already does this better (the installer
can't run the actual import; the app can), the same "two systems owning one decision" problem the
file-association design already avoids elsewhere.

What's real: reacting to an **already-installed copy of Paperbunkr itself**, mirroring how the
Visual C++ Redistributable installers behave when re-run against an existing install (named
directly as the reference pattern) — Repair or Uninstall, not a second full install wizard.

Mechanics, grounded via Inno's own registry convention (confirmed via jrsoftware.org and community
examples, not guessed) — Inno writes its uninstall entry to
`HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\{#SetupSetting("AppId")}_is1`, holding an
`UninstallString` value pointing at the existing install's own uninstaller and a standard
`DisplayVersion` value holding the version string that install was built with (the same
`{#MyAppVersion}` this project's own `Installer.iss` writes today, just from that earlier build) —
so `InitializeSetup()` can tell not just *whether* a previous copy exists but *which version* it is,
and branch three ways instead of two:

- **Not found** → return `True` immediately, Setup proceeds exactly as it does today, straight into
  the Welcome page — fresh installs are completely unaffected.
- **Found, `DisplayVersion` matches `{#MyAppVersion}` exactly** (re-running the same release's
  installer) → the Repair/Uninstall dialog, unchanged from the first pass of this decision:
  - **Uninstall** → `Exec` the found `UninstallString` (the *existing* installed version's own
    uninstaller runs, including whatever `[Code]` prompts that version shipped — e.g. the
    "delete my library data" opt-in this project's uninstaller already has), then
    `InitializeSetup` returns `False` — this Setup run exits without showing anything further. The
    user re-runs the new installer afterward if they still want to (re)install.
  - **Repair** → falls through to the normal wizard flow (see below).
- **Found, `DisplayVersion` differs** (upgrading from an older release — a beta point release, or
  the pre-2026-09-01 alpha) → this isn't a same-version repair, so the Repair/Uninstall framing
  doesn't fit; instead, a single lighthearted acknowledgment + proceed prompt, matching
  `WelcomeLabel1/2`'s established personality/tone rather than a generic "a previous version was
  found" system dialog. Sample copy (exact wording is an implementation-time detail, not frozen
  here): *"Oh hey, welcome back! Looks like you've already got {#MyAppName} {OldVersion} bunked in
  here. Ready to move up to {#MyAppVersion}? Click Next to upgrade, or Cancel to leave things as
  they are."* Reading the actual old version into the message means it naturally covers the
  alpha-era case too (an alpha `DisplayVersion` reads back verbatim, e.g. "0.1.1-alpha") without a
  separate alpha-specific code path — one parameterized message, not a branching matrix per era.
  **Next** falls through to the normal wizard flow (see below); **Cancel** aborts
  (`InitializeSetup` returns `False`) without touching anything.

**Falling through to the normal wizard** (shared by Repair and by the version-upgrade Next): Setup
proceeds into its normal flow starting at Welcome, with `UsePreviousAppDir=yes` set explicitly
(rather than relying on whatever Inno's own default is) so the wizard pre-fills the previously-used
install path — making both "repair" and "upgrade" functionally a guided reinstall over the existing
directory using the same file-copy machinery every install already goes through, not a separate
silent/no-wizard code path. This is a scoped, honest interpretation of "repair": Inno isn't MSI and
doesn't have a built-in maintenance-mode file-verification pass, so "repair" here means "guided
reinstall, defaulted to where you already have it," not silent background file-hash verification.

## Explicitly out of scope (YAGNI)

- Any further custom Pascal-scripted wizard pages beyond decision 7's Repair/Uninstall gate — no
  other specific need identified this round.
- Code-signing the installer exe — needs a certificate; flagged to the user, not pursued without
  one.
- Touching `Preferences → Advanced`'s own file-association UI or its underlying service beyond the
  `Program.cs` CLI-path fix in decision 5.
- Writing the `CHANGELOG.md` `[0.3.0-beta]` entry itself — separate release-prep work. (The
  installer no longer reads `CHANGELOG.md` at all after the 2026-09-09 correction that removed the
  `InfoBeforeFile` / `WhatsNew.txt` page — release notes are moving to the app's first-run Welcome
  screen. The changelog entry still needs to exist for a real release, but nothing in the
  installer depends on it now.)

## Files touched (for the implementation plan)

- `installer/Installer.iss` — wizard style/image/color directives, license/info-after directives,
  task list, `VersionInfo*` keys, version-fallback string (done), Welcome copy (done), and a new
  `[Code]` `InitializeSetup` for the Repair/Uninstall gate (decision 7).
- `installer/BuildInstaller.ps1` — combined-license-file generation (new, mirrors the existing
  `WhatsNew.txt` step), `InfoAfterFile` content generation (new, static), version-fallback string
  (done).
- `installer/Assets/WizardImage.png`, `WizardSmallImage.png` — done, produced this session.
  `WizardImage.bmp`, `WizardSmallImage.bmp`, `welcome-source.png` — to be deleted once
  `Installer.iss` is repointed.
- `src/Paperbunkr.App/Program.cs` — scope the `--register-file-associations`/
  `--unregister-file-associations` CLI path to the six comic-specific formats only.
- `src/Paperbunkr.App/Paperbunkr.App.csproj` — `<Version>` bump, done.
