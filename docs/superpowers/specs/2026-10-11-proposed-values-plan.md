# Proposed values — Implementation Plan
*Implements: docs/superpowers/specs/2026-10-11-proposed-values-design.md*

Facts read from the code: `EditIssuesWithHistory(description, ids, apply)` (`LibraryScreenViewModel.Rating.cs:89`) loads issues with Series and Tags only, so it needs an `includeProposals` option; `LibraryActionCatalog` builds menu entries from `ContextMenuEntry.Item(label, command, parameter, symbol)` and
orders them in `IssueMenuOrder`; `SmartListConditionViewModel` hides the operator box for `Duplicate` through `IsDuplicateField` and offers the special fields from `FieldOptionsFor`; `SmartScreen.axaml:162/170` bind operator and value visibility.
`PluginMatcherRegistry.Default` is empty today and `CblImportTests.ImportAny_AListWithNothingUsable…` relies on the real `findProposedValues` fixture being unmapped (it must move to another script).

## Step 1: `ProposedValues` helper
**Files:** `src/Paperbunkr.Data/Metadata/ProposedValues.cs` (new), `src/Paperbunkr.Data.Tests/ProposedValuesTests.cs` (new)
**What:** `CommittableFields`, `ParseFields(string?)`, `Pending(Issue, IReadOnlyCollection<MetadataProposalField>)`, `Commit(Issue, fields)`; latest Accepted proposal wins (by `CreatedAt`), as `AcceptedProposalValue` does.
**Verify:** accepted vs pending vs rejected, stored value present, each field, unparseable Count, case-insensitive field lists, unknown names ignored.

## Step 2: The smart-list field
**Files:** `SmartListField.cs` (append `HasProposedValue`), `SmartListQueryBuilder.cs` (special case), `SmartListConditionViewModel.cs` + `SmartScreen.axaml` (picker option, hide operator, value hint), `PluginMatcherRegistry.cs` (built-in `findProposedValues`), `SmartListSeriesFieldsTests`-style tests in `ProposedValueFieldTests.cs` (new), `CblImportTests.cs`/`CeImportRoutingTests.cs`/`CeSmartListReaderTests.cs` (adjust where `findProposedValues` was the unmapped example)
**Depends on:** Step 1
**Verify:** evaluation over a seeded library; a CE list using `findProposedValues` imports with the condition and no warning; the corpus run.

## Step 3: The bulk action
**Files:** `LibraryScreenViewModel.Rating.cs` (`includeProposals`), `LibraryScreenViewModel.DataTransfer.cs` (`CommitProposedValuesCommand`), `LibraryActionCatalog.cs` (entry + `IssueMenuOrder`), tests in `src/Paperbunkr.App.Tests` (new `CommitProposedValuesTests.cs`; existing catalog tests)
**Depends on:** Step 1
**Verify:** commit writes the fields, one undo restores them, "nothing to commit", remote books skipped; `LibraryActionCatalog*` tests.

## Step 4: Docs
`docs/paperbunkr-todo.md`, `wiki/Smart-Lists.md`, `wiki/The-Library.md` (the menu entry).
