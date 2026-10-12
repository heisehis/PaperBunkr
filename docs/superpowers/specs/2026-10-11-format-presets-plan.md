# Format from names, reading-order fixtures — Implementation Plan
*Implements: docs/superpowers/specs/2026-10-11-format-presets-design.md*

Facts read from the code: `FormatSignalCatalog.CeDefaultFormats` is the Format vocabulary (it classifies event signals; it does not infer formats); `Issue.EffectiveFormat()` reads an accepted proposal when the stored field is empty; `CblImport.ImportAny` is the entry point that tells reading lists from smart lists.

## Step 1: The guesser
**Files:** `src/Paperbunkr.Data/Metadata/FormatGuesser.cs` (new), `src/Paperbunkr.Data.Tests/FormatGuesserTests.cs` (new)
**Verify:** `FormatGuesserTests`.

## Step 2: The action
**Files:** `LibraryScreenViewModel.DataTransfer.cs` (`GuessFormatFromNameCommand`: compute in a read that includes proposals, apply through `EditIssuesWithHistory`), `LibraryActionCatalog.cs` (the *Tidy up* submenu), `SeriesBulkFixesTests.cs`
**Depends on:** Step 1
**Verify:** `SeriesBulkFixesTests`, `LibraryContextMenuBuilderTests`.

## Step 3: Reading-order fixtures
**Files:** `src/Paperbunkr.Data.Tests/TestData/ReadingOrders/*.cbl` (four files), `Paperbunkr.Data.Tests.csproj`, `src/Paperbunkr.Data.Tests/CeSmartLists/RealReadingOrderTests.cs` (new)
**Verify:** `RealReadingOrderTests`.
