# Scanner credits — Implementation Plan
*Implements: docs/superpowers/specs/2026-10-11-scan-credits-design.md*

Facts read from the code: nothing sets `Issue.ScanInformation` from a file name; `keymap.json` sits in the database's folder (`KeymapStore.DefaultPath`), and `PaperbunkrDbContext.GetDefaultDatabasePath()` honours the test override;
`OrganizeScrapeSettingsViewModel` already has line-list text boxes on its "Filters & names" tab, one `Load()` and one `SaveCommand`; `PreferenceIndex` lists each section's search entry by its XAML `Tag`.

## Step 1: Catalog, settings, detector
**Files:** `src/Paperbunkr.Data/Metadata/ScanCredits/ScanCreditCatalog.cs`, `ScanCreditSettings.cs`, `ScanCreditDetector.cs` (new), `src/Paperbunkr.Data.Tests/ScanCreditDetectorTests.cs` (new)
**What:** as the spec. The catalog's default instance must be lazy: its word lists are static fields declared after it.
**Verify:** `ScanCreditDetectorTests`.

## Step 2: The action
**Files:** `LibraryScreenViewModel.DataTransfer.cs` (`FindScannerFromFileNameCommand`: compute in a read that includes series and tags, apply through `EditIssuesWithHistory`), `LibraryActionCatalog.cs` (the *Tidy up* submenu)
**Depends on:** Step 1
**Verify:** `FindScannerActionTests`, `LibraryContextMenuBuilderTests`.

## Step 3: Preferences
**Files:** `OrganizeScrapeSettingsViewModel.cs` (`ScannerNames`, `ScanIgnoredWords`; loaded and saved with the page), `Views/Preferences/OrganizeScrapeSection.axaml` (a "SCANNER CREDITS" group), `Models/PreferenceIndex.cs`
**Depends on:** Step 1
**Verify:** the save/load test in `FindScannerActionTests`. The XAML is not viewed on screen.
