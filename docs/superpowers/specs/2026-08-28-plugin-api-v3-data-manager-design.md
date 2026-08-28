# Plugin API v3 — Metadata & Rules Access for Data-Manager-class Plugins

*Date: 2026-08-28. Scope: closes the gaps found in the plugin-extensibility gap analysis run
against Ehis's stated goal of building a future "Data manager mod plugin." Extends the Plugin
API v2 host (`docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md`) — does not replace or
restructure it. Depends on the companion `SmartList Engine v2` spec
(`docs/superpowers/specs/2026-08-28-smartlist-engine-v2-design.md`) for `IRulesEngine`'s nested
group support, but can ship independently if that spec lands later — `IRulesEngine` degrades
gracefully to flat-AND evaluation against the pre-v2 shape either way.*

## 1. Goals and non-goals

**Goal:** give a plugin the same three things the app's own UI has and a Data Manager plugin
would need — visibility into the relationship/event/continuity/age graph shipped in Phase
4a-4g, the ability to run the same rule-matching logic the Smart Lists screen runs, and a safe,
audited way to write metadata back — without widening `IPluginEnvironment`'s existing "curated
adapter, not raw access" philosophy (§4 of the v2 spec) into an uncurated one.

**Non-goals, each with its own reasoning:**
- **Not extending `CreateBookList` to let a plugin register a new `SmartListField` into the real
  Smart Lists engine.** That would mean letting a `.csx` script inject executable matching logic
  into a field-picker dropdown every user builds rules against by default — a materially bigger
  trust boundary than a plugin reading data or writing through curated setters. A Data Manager
  plugin's actual need (see `IRulesEngine` below) is *evaluating* rules against the data it has,
  not *contributing new field types* to the engine everyone else's smart lists use. If a real
  need for the latter emerges later, it deserves its own spec and its own explicit sign-off, not
  a rider on this one.
- **Not full adversarial sandboxing.** `.csx` scripts already run in-process with no AppDomain/
  process isolation (v2 spec §2/§3) — that boundary was never part of the design, and this spec
  doesn't add it. §6 below closes the *accidental* over-reach a well-meaning plugin author could
  stumble into today (reaching internal engine types because the assembly happens to be
  referenced), not a hardened boundary against a plugin author who's actively trying to escape
  it. That distinction is stated here explicitly so it's never quietly oversold later.
- **Not adding new hooks.** All of this rides the existing 17-hook taxonomy via new
  `IPluginEnvironment` members, invoked from the same command types that already exist.

## 2. Gap 1 — `IMetadataGraph`: read access to the relationship/event/age engine

**Current state:** none of Continuity, MediaRelation, StoryEvent, EventRelation, ComicAge, or
`SeriesFamilyResolver` are reachable through any `IPluginEnvironment` member. The five existing
sub-interfaces (`IApplication`/`IOpenBooksManager`/`IBrowser`/`IComicDisplay`/`IThemePlugin`)
predate all of Phase 3-4g.

**Design:** new sixth sub-interface, thin curated methods over the existing resolvers (no new
resolver logic — this is a read facade, same "adapter wraps an existing service" pattern
`IApplication` already uses for `LibraryFolderScanner`/`CoverThumbnailService`):

```csharp
public interface IMetadataGraph
{
    IReadOnlyList<MediaRelation> GetRelations(Series series);
    IReadOnlyList<Series> GetRelatedSeries(Series series);          // MediaRelationResolver
    IReadOnlyList<Continuity> GetContinuities(Series series);
    IReadOnlyList<Series> GetOtherSeriesInContinuity(Continuity continuity);  // ContinuityResolver
    IReadOnlyList<StoryEvent> GetEvents(Issue issue);                // EventMembershipResolver
    IReadOnlyList<EventMembership> GetMemberships(StoryEvent storyEvent);
    IReadOnlyList<EventRelation> GetEventRelations(StoryEvent storyEvent);   // EventRelationResolver
    (ComicAge? Age, decimal Confidence, string? Reason) GetAge(Issue issue); // BookAgeResolver
    IReadOnlyList<Series> GetSeriesFamily(Series series);            // SeriesFamilyResolver
}
```

`IPluginEnvironment.Metadata { get; }` exposes it. Real adapter (`PaperbunkrMetadataGraph`,
`Paperbunkr.App/Plugins/`) opens its own short-lived `PaperbunkrDb.CreateContext()` per call
(same per-call-context convention `PaperbunkrApplication` already uses) and delegates straight
to the existing resolver static methods — no new query logic, this spec is pure surface area.

## 3. Gap 2 — `GetLibraryBooks()`/`GetBook()` silently return incomplete Issues

**Current state:** `PaperbunkrApplication.GetLibraryBooks()`/`GetBook()` only
`.Include(i => i.Series)`. No lazy-loading proxies are configured anywhere in the app
(confirmed — no `UseLazyLoadingProxies` call exists), so `Issue.Tags`/`CustomValues`/
`MetadataProposals`/`Bookmarks` come back as **empty collections, not null and not an
exception** for every book a plugin reads today. Silent wrong data, not a crash — the worse of
the two failure modes for a plugin that trusts what it's handed.

**Design:** both methods add `.Include(i => i.Tags).Include(i => i.CustomValues)
.Include(i => i.MetadataProposals).Include(i => i.Bookmarks)` alongside the existing
`.Include(i => i.Series)`. Accepted eagerly, not behind an opt-in flag: `SmartListQueryBuilder`
already eager-loads this exact same Include set for the exact same "thousands not millions"
scale (verified — `ctx.Issues.Include(Series).Include(CustomValues).Include(MetadataProposals)
.Include(Tags).ToList()`), so this isn't a new performance tradeoff, it's adopting one the app
has already accepted elsewhere for the same data shape. Document on both methods that the
result set now matches what `SmartListQueryBuilder` sees, since that equivalence is what makes
`IRulesEngine` (§4) trustworthy — a plugin evaluating a rule against `GetLibraryBooks()`'s
output and a plugin asking `IRulesEngine` to evaluate the same rule should see the same
underlying data either way.

## 4. Gap 3 — `IRulesEngine`: reuse the app's own matcher instead of reimplementing it

**Current state:** no plugin path to `SmartListCatalog`/`SmartListQueryBuilder` at all. A Data
Manager plugin wanting to act on "everything matching some condition" has to hand-roll filtering
in its script, with no guarantee it agrees with what the real Smart Lists screen would compute
for what the plugin's author believes is "the same" rule.

**Design:** a lightweight, plugin-facing condition DTO — deliberately not the EF entities
themselves, so a plugin can build and evaluate a throwaway rule without needing any write access
to the `SmartList`/`SmartListCondition` tables:

```csharp
public sealed record PluginCondition(SmartListField Field, SmartListOperator Op, string Value,
    string? Value2 = null, string? CustomValueName = null, int? VirtualTagId = null,
    SearchMode? SearchMode = null, bool Not = false, bool IgnoreCase = true);

public sealed record PluginConditionGroup(SmartListGroupMode Mode,
    IReadOnlyList<PluginCondition> Conditions, IReadOnlyList<PluginConditionGroup> ChildGroups);

public interface IRulesEngine
{
    IReadOnlyList<Issue> Evaluate(PluginConditionGroup rule);
    IReadOnlyList<Issue> EvaluateSmartList(int smartListId);   // run an existing saved SmartList by Id
}
```

Real adapter translates `PluginConditionGroup`/`PluginCondition` into the exact in-memory shape
`SmartListQueryBuilder.Build` already consumes and calls it directly — zero duplicated matching
logic, by construction. If the companion SmartList Engine v2 spec hasn't landed yet when this
ships, `PluginConditionGroup` still compiles and works — it just always builds a single
top-level group (`ChildGroups` empty in practice), which is exactly today's flat-AND shape.
`EvaluateSmartList` is the common case for a Data Manager plugin: "give me what Smart List #N
currently matches" without re-describing the rule at all.

## 5. Gap 4 — `IMetadataWriter`: a curated, audited write surface

**Current state:** `IApplication` can `RemoveBook` and `SetCustomBookThumbnail` — nothing to
edit a metadata field. A plugin whose entire premise is data management currently cannot write
any data back through a sanctioned path.

**Design:** narrow, per-field setters — not a generic "set any property by name" method, so the
compile-time surface documents exactly what a plugin is allowed to touch, and so each setter can
carry its own validation:

```csharp
public interface IMetadataWriter
{
    bool SetFormat(Issue issue, string? value);
    bool SetBookAge(Issue issue, string? value);
    bool SetCustomValue(Issue issue, string name, string? value);
    bool AddTag(Issue issue, string tag);
    bool RemoveTag(Issue issue, string tag);
}
```

Each method: opens its own `PaperbunkrDb.CreateContext()`, loads the tracked entity by Id
(mirroring `PaperbunkrApplication.RemoveBook`'s existing `context.Issues.Find(issue.Id)`
pattern — never trusts the caller's possibly-stale/detached `Issue` instance for anything but
its Id), applies the change through normal EF change-tracking, `SaveChanges()`s, and logs via
`DiagnosticsService.LogMilestone($"Plugin '{pluginKey}' set {field} on Issue #{id}")` — same
audit primitive `PaperbunkrApplication.Restart()` already uses, now given an actual audit
purpose. Returns `false` (never throws) if the Issue no longer exists, matching `RemoveBook`'s
existing contract.

**Confirmation gating for bulk writes:** a manifest `Command` can declare
`confirmWrites="true"` (new optional `CommandManifestEntry`/`Command` attribute, default false).
When true, `PluginEngine.InvokeAsync` requires the command to have already produced an
affirmative `AskQuestion` response in the same invocation before any `IMetadataWriter` call
succeeds — enforced by having `IMetadataWriter`'s adapter check a per-invocation "confirmed" flag
that `IApplication.AskQuestion` sets to true only when called by a command whose manifest
declares `confirmWrites`. This reuses the existing native dialog primitive (no new UI) and means
a bulk-editing Data Manager command is *structurally* required to ask the user before it can
touch more than a preview — not just documented as expected to.

## 6. Gap 5 — persistent plugin config

**Current state:** `IPluginConfig` is exactly one read-only member, `LibraryPaths`. The
`ConfigScript` hook exists specifically to open "a settings dialog from a gear icon" (v2 spec
§5), but there's nowhere for that dialog's script to persist what the user set.

**Design:** new sparse-table entity, same convention as `PluginCommandState`:

```
PluginSettingState
    Id
    PluginKey
    Key          // setting name, plugin-defined
    Value        // string; plugin's own responsibility to parse/format
```

`IPluginConfig` gains `string? GetSetting(string key)` / `void SetSetting(string key, string
value)`, scoped automatically to the calling command's own `PluginKey` (the adapter reads it off
`IPluginEnvironment.CommandPath`'s owning manifest, same way `Command.Initialize` already scopes
`CommandPath` per-command) — one plugin can't read or overwrite another's settings by construing
a key collision.

## 7. Gap 6 — sandbox hardening (accidental overreach, not adversarial isolation)

**Current state:** `Paperbunkr.Plugins.csproj` references the whole `Paperbunkr.Data.csproj`,
not just its `Entities` namespace. Every public type in `Paperbunkr.Data` — including
`SmartListQueryBuilder`, `ContinuityResolver`, `BookAgeResolver`, `SeriesFamilyResolver`,
`MediaRelationResolver`, `EventMembershipResolver`, `PaperbunkrDbContext` itself (public
constructor) — is reachable from a `.csx` script by fully-qualified name today, entirely outside
the curated `IPluginEnvironment` surface this spec (and v2) just spent five sections building.
`PaperbunkrDbContext.GetDefaultDatabasePath()` is also public. A script that adds a `#r
directive for an already-in-process-loaded EF Core assembly could plausibly open its own raw
context against the live SQLite file, bypassing `LibraryDeletionHelper`'s cascade-safety path
entirely.

**Design:**
- Change the query-builder and every metadata resolver class (`SmartListQueryBuilder`,
  `ContinuityResolver`, `MediaRelationResolver`, `BookAgeResolver`, `SeriesFamilyResolver`,
  `EventMembershipResolver`, `EventRelationResolver`, `EventSuggestionResolver`) from `public` to
  `internal`, with `[InternalsVisibleTo("Paperbunkr.App")]` and `[InternalsVisibleTo(
  "Paperbunkr.Data.Tests")]` on `Paperbunkr.Data.csproj` — deliberately **not** granted to
  `Paperbunkr.Plugins`. This is a compile-time fence: `Paperbunkr.App`'s new `IMetadataGraph`/
  `IRulesEngine` adapters (§2, §4) still compile fine against these types (same assembly-internal
  visibility any other internal service already gets), but a `.csx` script referencing
  `Paperbunkr.Data.dll` can no longer resolve them at all, closing the accidental-reachability
  gap at the same boundary C#'s own access modifiers already exist to enforce — no custom
  reflection-blocking or AppDomain work needed.
- `PaperbunkrDbContext`'s constructor and `PaperbunkrDbContextFactory` become `internal` the
  same way (adjusting `IDesignTimeDbContextFactory<T>`'s public contract requirement — verify at
  implementation time whether EF's design-time tooling still discovers an internal factory type
  in the same assembly; if not, keep the factory itself public but make the `DbContext`
  constructor `internal` and have the factory (which lives in the same assembly and thus can
  still call it) remain the only public entry point external tooling needs).
- In `CSharpCommand.PreCompile`'s `ScriptOptions`, explicitly confirm (via a unit test compiling
  a script containing a `#r "Microsoft.EntityFrameworkCore"` directive) that Roslyn scripting's
  default resolver behavior does not let an arbitrary already-loaded assembly resolve into scope
  this way; if it does, pin down the exact `ScriptOptions`/`MetadataReferenceResolver`
  configuration needed to disable directive-driven assembly loading entirely, since the fixed
  `.WithReferences(...)` list is meant to be the *complete* set a script can ever see.
- Document explicitly, in both this spec and `wiki/Plugins.md`, that this closes accidental
  scope creep by a well-meaning plugin author — not the adversarial case of someone deliberately
  trying to break out of the compiled reference set via reflection (`Type.GetType` +
  `Activator.CreateInstance` against a fully-qualified internal type name can still technically
  succeed against types in the same non-sandboxed process; true isolation would need AppDomain/
  process-level separation, which is out of scope per §1's non-goals).

## 8. Testing

- Unit tests: `IMetadataGraph` adapter methods against a fixture DB seeded with Phase 4a-4g data
  (Continuity/MediaRelation/StoryEvent/EventRelation), asserting parity with what the Story
  Events screen's own view models would show for the same fixtures.
- `GetLibraryBooks()`/`GetBook()` regression test: assert `Tags`/`CustomValues`/
  `MetadataProposals`/`Bookmarks` are populated (not empty) for a fixture Issue that has them.
- `IRulesEngine.Evaluate`/`EvaluateSmartList` parity test: for a fixture `SmartList`, assert
  `EvaluateSmartList(id)` and the Smart Lists screen's own `SmartListQueryBuilder.Build` call
  return the identical Issue set — the whole point of this interface is that equivalence.
- `IMetadataWriter`: happy-path field writes persist and are visible to a subsequent
  `GetBook()`; `confirmWrites="true"` command's writer calls fail (return false, no DB change)
  until `AskQuestion` has been called and answered affirmatively in that invocation; audit log
  line is written via `DiagnosticsService.LogMilestone` for every successful write.
- `PluginSettingState`: two plugins writing the same `Key` don't collide; a command's `GetSetting`
  only ever sees its own `PluginKey`'s rows.
- Sandbox test: a `.csx` fixture script that references `SmartListQueryBuilder`,
  `ContinuityResolver`, or `PaperbunkrDbContext` by fully-qualified name fails to *compile*
  (assert `Command.IsBroken`/`CompileError` is set, not that it throws at invoke time) — this is
  the regression guard for §7's whole point.
- Integration test: end-to-end Data-Manager-shaped fixture plugin — `Startup` hook reads
  `IMetadataGraph.GetSeriesFamily` for a fixture series, `Library` hook command runs
  `IRulesEngine.EvaluateSmartList` and then `IMetadataWriter.AddTag`s each match with
  `confirmWrites="true"` declared, asserting the confirm-gate actually blocks the write until
  `AskQuestion` is answered.
