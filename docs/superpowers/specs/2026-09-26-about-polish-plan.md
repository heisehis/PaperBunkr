# About Polish — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-26-about-polish-design.md*

Working tree is shared with other sessions (`PreferencesScreenViewModel.cs`, its tests, and the csproj
already carry their uncommitted edits): use targeted `Edit`s only, never whole-file rewrites of those.

## Step 1: MarkdownLite parser
**Files:** `src/Paperbunkr.App/Services/MarkdownLite.cs` (new), `src/Paperbunkr.App/Services/LegalDocumentParser.cs` (delete),
`src/Paperbunkr.App.Tests/MarkdownLiteTests.cs` (new), `src/Paperbunkr.App.Tests/LegalDocumentParserTests.cs` (delete)
**What:** `MdBlock`/`MdRun`/kinds, `Parse`, `Preformatted`, `ParseInline` per spec §1.
**Depends on:** none
**Verify:** `MarkdownLiteTests` (TDD).

## Step 2: Changelog model
**Files:** `Services/ChangelogBodyFormatter.cs` (edit), `Services/ChangelogSelection.cs` (new, also holds `ChangelogRow`),
`ChangelogBodyFormatterTests.cs` (edit), `ChangelogSelectionTests.cs` (new)
**What:** groups carry `MdBlock`s + `ChangelogTagKind`; `Visible`, `Current`, `BuildRows`.
**Depends on:** Step 1
**Verify:** unit tests, including the real `CHANGELOG.md`.

## Step 3: MarkdownView control + link resolver
**Files:** `Controls/MarkdownView.cs` (new), `Services/LinkTargetResolver.cs` (new), `LinkTargetResolverTests.cs` (new),
`MarkdownViewTests.cs` (new, headless)
**What:** code-only renderer per spec §2; DynamicResource brushes; link buttons.
**Depends on:** Step 1
**Verify:** headless render test + resolver tests.

## Step 4: ChangelogEntryView + What's New
**Files:** `Views/ChangelogEntryView.axaml` + `.axaml.cs` (new, together), `Views/WhatsNewOverlay.axaml`,
`ViewModels/WhatsNewOverlayViewModel.cs`, `ViewModels/MainViewModel.cs` (`OpenWhatsNewOverlayCurrentOnly`),
`Views/AboutSectionConverters.cs` (delete), `AboutSectionConvertersTests.cs` (delete), `WhatsNewOverlayViewModelTests.cs` (edit)
**Depends on:** Steps 2, 3
**Verify:** tests; forced CoreCompile rebuild (new view).

## Step 5: Update-available overlay
**Files:** `ViewModels/UpdateAvailableOverlayViewModel.cs`, `Views/UpdateAvailableOverlay.axaml`, `MainViewModel.cs`
(startup check), `Services/ProjectLinks.cs` (new), `UpdateAvailableOverlayViewModelTests.cs` (new or edit)
**Verify:** release-notes URL test.

## Step 6: About view model
**Files:** `ViewModels/PreferencesScreenViewModel.About.cs` (new partial), `PreferencesScreenViewModel.cs` (remove moved
members: ChangelogEntries, RefreshChangelog, legal viewer members; ctor init), `SectionTabs.cs` (About reveal),
`Models/PreferenceIndex.cs`, `Services/LegalDocuments.cs` (new registry), `Services/AboutInfo.cs` (new),
`PreferencesScreenViewModelTests.cs` (edit About tests), `AboutInfoTests.cs` (new)
**What:** `AboutTabs`, `ChangelogRows`, legal viewer state (doc, blocks, updated, doc-link swap), copy commands with
"Copied" state, project link + folder commands.
**Depends on:** Steps 2, 5
**Verify:** VM tests + PreferenceIndexTests.

## Step 7: About + viewer XAML
**Files:** `Views/Preferences/AboutSection.axaml`, `Views/LegalDocumentViewerOverlay.axaml`
**Depends on:** Steps 3, 4, 6
**Verify:** build (forced CoreCompile), `avalonia-pro-max/review-checklist`.

## Step 8: Texts
**Files:** `TERMS.md`, `PRIVACY.md`, `COMICVINE_NOTICE.md`, `THIRD-PARTY-NOTICES.md` (new), `CHANGELOG.md`,
`src/Paperbunkr.App/Paperbunkr.App.csproj` (copy notices), test csproj if it copies docs
**Verify:** MarkdownLite real-file theory (no literal `**`); installer strip regex still covers the syntax used.

## Step 9: Docs + full verification
**Files:** `docs/paperbunkr-todo.md`, `wiki/Preferences.md` (About section, if any)
**Verify:** full App test project run; launch app is the user's on-screen check.
