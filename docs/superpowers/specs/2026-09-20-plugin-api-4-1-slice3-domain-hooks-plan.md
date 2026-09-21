# Plugin API 4.1 — Slice 3 (domain event hooks) — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-20-plugin-api-4-1-design.md §5 (as revised — §5.3/§5.4 describe what shipped).*

> **Status: implemented 2026-09-20.** Verified: Plugins.Tests 127/127; Data.Tests reading-list subset
> 72/72; the 48 new App tests pass; the wider App regression subset is clean apart from failures that
> also occur on untouched `HEAD` (see the roadmap note). **Not verified:** any of this against a real
> running app with a real plugin.

## What the survey changed
- **No aggregated repository exists**, and several writers use a **caller-owned context whose
  `SaveChanges` belongs to a larger unit of work** (`LibraryDeletionHelper`, `ArcReadingListBuilder`,
  `AddIssuesToReadingList`). The spec's "manager owns its own save" can't serve them. Replaced by a
  manager that *stages* an announcement on the context, released by `PaperbunkrDbContext` only after a
  successful save (`RunAfterSave`).
- Scanners, Library Health and the recorder are constructed in many places with no DI container, so
  producers raise on a `LibraryEvents` hub they take as an optional parameter (default: a shared
  instance), matching their `contextFactory` convention.
- The spec's claim that the scanner counts updates was wrong (that counter is `SyncMetadataAsync`'s).
  `UpdatedItemIds` ships as the empty reserved collection the decision called for.
- Confirming "once per confirmation" for `MissingFileDetected` needed transition detection
  (count crosses the threshold), not "count ≥ threshold", which would re-fire every Verify pass.

## Steps (all done)
1. **Hook definitions** — `PluginHooks` (4 constants, `DomainEventHooks`, `Since` 4.1),
   `PluginGlobals` (`CancellationToken` on the base; `BookRead`/`LibraryScanCompleted`/
   `MissingFileDetected`/`ReadingListChanged` globals), `PluginGlobalsTypeMap`.
2. **Event hub + post-save hook** — `Paperbunkr.Data/Events/LibraryEvents.cs`;
   `PaperbunkrDbContext.RunAfterSave` + `SaveChanges`/`SaveChangesAsync` overrides.
3. **`ReadingListManager`** — `AddIssues`, `RemoveItems`, `MoveItem`, `RemoveIssueFromAllLists`,
   `RecordCreatedWithItems`, `Record`; every production write site migrated (see spec §5.4) and audited.
4. **Producers** — `LibraryFolderScanner.ScanAll` → `LibraryScanCompleted`; `LibraryHealthService` →
   `MissingFileConfirmed` (transition only, after save); `IReadingEventRecorder.ReadingFinished`
   (default-no-op interface event so test doubles needn't implement it) raised after the row is saved.
5. **`DomainHookDispatcher`** (`Paperbunkr.Plugins`) — per-command serial lane, bounded queue of 16
   dropping the oldest, cooperative `CancellationToken` at a 30 s timeout, hung-command handling, each
   problem kind reported once per command per session.
6. **Host wiring** — `PluginHostService` subscribes (`AttachDomainEvents`), builds typed globals, loads the
   `Issue`/`Book` for `BookRead`, and turns dispatcher problems into plugin-scoped Activity alerts.
7. **Tests + docs** — see the test files listed in the roadmap note; wiki + spec updated.

## Tests
`DomainHookDispatcherTests` (real `.csx` plugins: fire-and-forget, order, isolation, once-only reporting,
cooperative cancel, hung lane, bounded queue, 200-event burst); `ReadingListManagerTests` (no announcement
before the save, none for an abandoned or failed save, one per operation, dedupe, edge cases);
`DomainEventProducerTests` (scanner, Library Health transition logic, recorder, deletion);
`PluginHostDomainHookTests` (payload mapping per hook, lifecycle, plugin-scoped alerts).
