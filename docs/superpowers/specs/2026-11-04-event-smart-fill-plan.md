# Event Smart fill — implementation plan

Implements [the approved design](2026-11-04-event-smart-fill-design.md). Baseline `28e60a1`; preserve all unrelated working-tree changes. Actual projects target .NET 10 and Avalonia 12.1.1 despite the older onboarding shorthand. Do not run builds/tests concurrently.

## 1. Persist field state, precise endpoints and history

**Files:** new `src/Paperbunkr.Data/Entities/EventMetadataState.cs`, `EventDateEndpoint.cs`, `EventMetadataChange.cs`; new `src/Paperbunkr.Data/Metadata/EventEnrichment/EventMetadataStore.cs`, `EventPublicationDate.cs`; edit `Entities/StoryEvent.cs`, `PaperbunkrDbContext.cs`; new EF migration and updated model snapshot.

Implement per-field protection, revision and provenance, namespaced GCD arc identity, endpoint components and local before/after history. Use a context save hook to advance revisions for tracked manual/legacy writes, including collections; central service writes carry their own atomic state/history. Existing values remain authoritative; no speculative backfill. Expose one effective-date accessor. Follow EF migration conventions without touching unrelated pending migration changes.

**Depends on:** none. **Verify:** new Data tests for partial-date round-trip, clearing protection, revisions, FK cascade, exact legacy fallback and migration Up/Down scoped to the new migration.

## 2. Deterministic decisions and publication intervals

**Files:** new `Metadata/EventEnrichment/EventEnrichmentModels.cs`, `EventEnrichmentEvaluator.cs`, `EventDescriptionText.cs`; tests in `Paperbunkr.Data.Tests/EventEnrichment/`.

Separate evidence from decisions. Implement blank/protected semantics, equal-quality conflicts, synopsis versus bibliography cleanup, dedicated cover ordering, bounded whole-event classifications and no indiscriminate tag copying. Calculate date endpoints over precision intervals with coverage/uncertainty/provisional metadata; never manufacture calendar components. Test exact-day plus same-month ambiguity and protected endpoint ordering.

**Depends on:** 1 for value types only. **Verify:** pure tests and source-order permutation checks; no network or full dump.

## 3. Optional GCD schema 2 with schema-1 reading

**Files:** `Gcd/GcdModels.cs`, `GcdExtractor.cs`, `GcdDataStore.cs`; new `Gcd/GcdEventData.cs`; `tools/Paperbunkr.GcdExtract/Program.cs`; Data GCD tests and tiny synthetic SQLite fixture.

Add compact arcs, membership/reprint/edition evidence, publication formats and date uncertainty. Keep old series/bonds/date APIs and schema-1 reads. Add indexed arc queries and verified-member lookups, not fuzzy series unions. Version output artifact/manifest paths so schema-1 clients keep their supported manifest. Resolve generic content types by model identity when needed; do not export arbitrary issue tags as event tags.

**Depends on:** 2 date value types. **Verify:** schema1/schema2 fixture tests, excluded reprint/collected-edition and partial-date cases. Later run extraction against the user's read-only temporary full-dump copy to prepare local artifacts; never commit/publish the dump or covers.

## 4. Provider adapters and read-only collection

**Files:** `ReadingLists/Sources/ArcModels.cs`, `IReadingListSource.cs`, `MetronSource.cs`, `ComicVineSource.cs`; new `Metadata/EventEnrichment/EventEvidenceCollector.cs`; existing `ReadingListSourceRegistry.cs`; new adapter tests.

Add optional fields/default-compatible evidence methods so unrelated source adapters and test fakes still work. Retain deck separately from long description, issue date precision, IDs and complete-list status. Reuse shared HTTP/auth/quota code, cancellation and pagination. Collect existing linked identities and GCD verified bridges; show unresolved identities without persisting guesses. Cache repeated reads within an operation. Classify errors/unconfigured sources separately from absent data.

**Depends on:** 2–3. **Verify:** fake HTTP/source fixtures for pagination, deck fallback, cancellation, identity conflict and incomplete responses.

## 5. Apply, compare and conditional undo

**Files:** new `Metadata/EventEnrichment/EventEnrichmentService.cs`; `EventMetadataStore.cs`; new Data integration tests following `StoryEventIdentity/IdentityTestDb.cs` and `ReadingLists/Matching/ReadingListReviewService.cs` transaction patterns.

Snapshot field revisions and identity/member fingerprints before gathering evidence; re-read inside the per-event transaction. Apply only still-eligible fields; explicit replacements require selected candidates. Save provenance/history with values. Undo uses revisions, restores complete field snapshots, protects returned blanks, is idempotent and skips later edits/merged events. Cancellation preserves prior committed events.

**Depends on:** 1–4. **Verify:** stale writes, transaction rollback, intervening equal-value edits, two-context races, repeat undo, collections/dates/cover metadata and restart persistence.

## 6. Integrate existing writers, merges and date consumers

**Files:** `Services/StoryEventDetailsCompletion.cs`, `ViewModels/NewEventOrContinuityViewModel.cs`, `Metadata/StoryEventMerger.cs`, `EventChronology.cs`, `EventChronologyInference.cs`, `EventSuggestionResolver.cs`, `EventRelationSuggestionResolver.cs`, `EventOrderSweep.cs`, `SeriesConnector/SeriesConnectorSnapshot.cs`, `ViewModels/DetailTabsViewModel.SeriesConnector.cs`, `ContinuitySidebarViewModel.cs`.

Use shared protection in legacy completion and guided provider setup; explicit refresh adopts Smart fill semantics. Keep automatic completion limited to its existing description/cover scope. Preserve survivor metadata/protection in merges and invalidate pre-merge undo safely. Route all date readers through effective partial dates while preserving existing read-only member fallbacks. Do not rewrite unrelated DbContext/rating changes.

**Depends on:** 5. **Verify:** existing event identity/details/chronology tests plus new integration regressions for each bypass and merged history.

## 7. GCD installer capabilities and upgrade detection

**Files:** `Services/Gcd/GcdDataInstaller.cs`, GCD Connections view model/view located in survey, `gcd-data.json` only if a verified artifact is prepared; installer tests.

Accept both supported schemas, detect same-date schema/capability upgrades, use a separate schema2 manifest URL, retain schema1 fallback and atomic install validation. Display installed snapshot/capability. Prepare downloadable schema2 artifact and manifest locally; external publication is a separate delivery action, not a fabricated available download.

**Depends on:** 3. **Verify:** same-date upgrade, unsupported schema, failed hash/staging install retaining old data and existing installer regressions. Avalonia MVVM/data-binding/style guidance governs Connections edits.

## 8. App orchestration and editor draft integration

**Files:** new `Services/EventSmartFillRunner.cs`, `ViewModels/EventSmartFillViewModel.cs`, `NewEventOrContinuityViewModel.SmartFill.cs`; edit editor main view model, `EventPageViewModel.cs`, event/sidebar action builders and selection owners located in survey; `StoryEventCoverCache.cs`.

Share collection/application logic across explicit single/batch runs; stage editor values/provenance until Save. Cancel/close/navigation guard late responses. Prepare and decode immutable cover candidates before commit; retain prior manual artwork for undo. Bind progress/cancel/result/history state. Do not introduce another HTTP limiter or change event membership.

**Depends on:** 5–7. **Verify:** focused App tests for draft cancel/save, stale edit during fetch, batch cancellation, UI thread dispatch and failed-cover preservation. Governing skills: Avalonia MVVM, data binding and events.

## 9. Compare sources, history and entry points

**Files:** new `Views/EventSmartFillOverlay.axaml` and matching `.axaml.cs` together; new comparison/history row view models as needed; edit `Views/NewEventOrContinuityOverlay.axaml`, `EventHero.axaml`, owning screen overlay host/navigation and selected-event toolbar/menu.

Reuse DialogCard, common buttons/tokens, accessible labels, responsive stacked values, explicit unselected replacements and protected-blank Fill again. Display date precision/coverage, source links, original-versus-issue artwork labels, history and conditional Undo. Wire `IInputService`/screen input registration and focus return. Defer row removal/overlay closes after routed events. No local styles/hardcoded colours or new hotkey handlers.

**Depends on:** 8. **Verify:** headless editor/comparison/history tests, real MainWindow keyboard tests and Default/Daylight/Matrix renders at 1366x768/narrow hosts. Governing skills: Paperbunkr design language; Avalonia XAML, layout, styling, accessibility, testing, and pro-max/review-checklist.

## 10. Complete validation and documentation

**Files:** focused new/existing Data/App test classes, approved spec/plan, `docs/paperbunkr-todo.md`.

Run new pure/Data tests first, then changed writer/chronology/GCD tests, then App behavior and shared lint filters. Build/launch compiled XAML honestly; after a failed weave force CoreCompile stale before retry. Run the documented `Speed!=Slow` selection when focused checks pass; record resource/existing failures separately. Perform real-window keyboard/theme/cover checks and review-checklist before calling UI done. Update roadmap with verified work and commit references; do not label incomplete delivery or external artifact publication as shipped.

**Depends on:** 1–9. **Done when:** all twelve spec acceptance cases have evidence; no implementation placeholders; external distribution artifacts and any remaining manual limitations are stated accurately.

## Progress

- Steps 1–9 are implemented in the working tree. New-event provider setup remains the existing explicit setup flow; Smart fill is available on the saved event page and in direct Edit details, where it stages changes until Save.
- Persistence includes migration `20261104091810_AddEventSmartFill`. Field revisions cover legacy writers and manual cover replacements, and protected partial dates flow through the date consumers.
- Provider collection keeps synopsis/deck alternatives, rejects contradictory cross-provider bridges, caches source reads within a batch, and treats unverified original-edition spans as comparison suggestions.
- Final Data selection: 220/220 passed (`EventEnrichment|Gcd|StoryEventIdentity|EventSuggestionResolverTests|SeriesConnector`), including legacy exact-date synchronization over partial endpoints, transaction cancellation/rollback, stale identity/revisions, persistent undo and migration Up/Down. Transcript: `C:/Users/DeeDee/AppData/Local/Temp/paperbunkr-event-data-verified.log`.
- Final App selection: 105/105 passed with `Speed!=Slow`, including editor save/cancel/clear, failed-cover preservation, GCD installer, six theme/width renders and Escape in a headless real MainWindow, after the closing-animation change. Transcript: `C:/Users/DeeDee/AppData/Local/Temp/paperbunkr-event-app-verified.log`. The earlier 106-case selection exposed the pre-existing `FamilyButtonsCarryNoSizeOverrides` lint failure: four Height attributes in the two untouched Scraper review dialogs; that known baseline case is excluded from the final run. App and compiled XAML built successfully; `git diff --check` passed.
- The user's 2026-09-15 dump produced a schema-2 extract: 147.7 MB SQLite, 44.0 MB zip, 582 arcs, 142 descriptions, 3,416 arc-to-issue rows. ZIP CRC, SQLite `quick_check` and manifest SHA-256 checks passed. The Timestorm collected edition is separately flagged; X-Men retains `2009-08-00`.
- Artifact SHA-256: `ce55198ee234d341d4e10f3f34cbee84e3908f42e77773490f7b4cab46020167`. Zip, manifest, verification report and attribution README are saved under `C:/Users/DeeDee/Downloads/Paperbunkr-GCD-v2-2026-09-15`; not published. The shipped schema-1 manifest remains unchanged; the app can fall back to it until versioned schema-2 data is published. The temporary 6.7 GB full-dump copy was removed after verification; the user's original archive is untouched.
- Reviewed Avalonia pro-max/review-checklist: new controls use semantic tokens/shared styles, text status labels, modal input and deferred close/removal. Theme/layout and MainWindow keyboard behavior are covered by tests. Interactive Narrator, OS high-contrast/DPI and native live-provider visual sign-off are not claimed. No release/installer/slow-suite validation is claimed.
