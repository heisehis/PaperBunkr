# Metadata Model — Phase 4e: Format-Signal Event Suggestions

**Date:** 2026-08-27
**Status:** Approved, pending implementation
**Source doc:** Design session with Ehis (2026-08-27), continuing [[phase4b-story-events]].
Grounded directly against ComicRack CE's real shipped defaults
(`_reference/ComicRackCE/ComicRack/Output/DefaultLists.txt`, `[Book Formats]` section) rather than
assumed.

## Context

`Issue.Format` is a free-text field, ported from CE's `ComicInfo.Format` (verified:
`ComicRack.Engine/ComicInfo.cs`), already present in Paperbunkr's schema and already wired into
`SmartListCatalog` as a filterable text field — but dormant otherwise, no editor UI, and (confirmed
by grep) no default value list ported from CE at all yet.

CE's own shipped default list (`DefaultLists.txt`, `[Book Formats]`) is 16 values: `1/2`, `Annual`,
`Black & White`, `Director's Cut`, `Epilogue`, `Giant`, `King`, `Minus 1`, `Sketch`, `Special`,
`Preview`, `Prologue`, `Trade Paper Back`, `One Shot`, `Web Comic`, `Hardcover`. These aren't all
equally useful signals of "this issue is part of something bigger than its own series" — `Trade
Paper Back`/`Hardcover`/`Web Comic`/`Black & White`/`Director's Cut` describe packaging/edition, not
narrative role, and carry no event signal at all. This phase's actual scope is narrower than
"import the Format field" sounds: import the full CE vocabulary as the field's autocomplete list
(parity, cheap, matches how `DefaultGenres`/`DefaultBookAges` etc. already seed other free-text
fields' autocomplete in CE), but only *act* on the subset that's a genuine signal.

Two of the sixteen values are not just a loose signal — `Prologue` and `Epilogue` are also two of
the six values in Paperbunkr's own already-shipped `EventMembershipRole` enum ([[phase4b-story-
events]]). An issue whose `Format` is `Prologue` isn't just "maybe event-relevant," it's a
near-literal match to a specific `EventMembershipRole` — worth pre-filling, not just flagging.

This phase does not create any new relation/membership automatically — it only surfaces a
reviewable queue, same "propose, don't assert" posture the `MetadataProposal` system (Phase 2a) and
Phase 3's manual-only relation creation both already established as this codebase's norm.

## Scope

### `FormatSignalCatalog`

New static catalog, `src/Paperbunkr.Data/Metadata/FormatSignalCatalog.cs`, classifying the CE
default vocabulary (field-descriptor-dictionary idiom, matching `LibraryFieldCatalog`/
`RelationTypeCatalog`'s existing shape in this codebase):

```csharp
public enum FormatSignalStrength { None, Weak, Strong }

public sealed record FormatSignalInfo(FormatSignalStrength Strength, EventMembershipRole? SuggestedRole);

public static class FormatSignalCatalog
{
    public static readonly IReadOnlyDictionary<string, FormatSignalInfo> Defaults = new Dictionary<string, FormatSignalInfo>(StringComparer.OrdinalIgnoreCase)
    {
        ["Prologue"] = new(FormatSignalStrength.Strong, EventMembershipRole.Prologue),
        ["Epilogue"] = new(FormatSignalStrength.Strong, EventMembershipRole.Epilogue),
        ["Annual"] = new(FormatSignalStrength.Strong, null),
        ["Special"] = new(FormatSignalStrength.Strong, null),
        ["One Shot"] = new(FormatSignalStrength.Strong, null),
        ["Minus 1"] = new(FormatSignalStrength.Weak, EventMembershipRole.Prologue),
        ["Giant"] = new(FormatSignalStrength.Weak, null),
        ["King"] = new(FormatSignalStrength.Weak, null),
        ["1/2"] = new(FormatSignalStrength.Weak, null),
        ["Preview"] = new(FormatSignalStrength.Weak, null),
        // Sketch, Trade Paper Back, Hardcover, Web Comic, Black & White, Director's Cut:
        // not listed -> Strength.None (packaging/edition only, no event signal).
    };
}
```

`Minus 1` gets `EventMembershipRole.Prologue` as its suggested role, not a coincidence — Marvel's
actual 1997 "-1" event was a company-wide prequel crossover built entirely around that Format
value, a real precedent for treating it as a prequel-role signal rather than a generic flag. A
value not in this dictionary (including anything a user typed that isn't part of CE's default list
at all) resolves to `FormatSignalStrength.None` via `Defaults.GetValueOrDefault`.

### `EventSuggestionResolver`

New file, `src/Paperbunkr.Data/Metadata/EventSuggestionResolver.cs`:

```csharp
public static class EventSuggestionResolver
{
    public static IReadOnlyList<EventSuggestion> GetSuggestions(PaperbunkrDbContext context, int storyEventId);
}

public sealed record EventSuggestion(Issue Issue, FormatSignalStrength Strength, EventMembershipRole? SuggestedRole, string Reason);
```

Candidate issues for a given event: not already an `EventMembership` for that event, `Issue.Format`
resolves to `Strong` or `Weak` via `FormatSignalCatalog`, and **either** the issue's `Year` falls
within the event's `StartDate`/`EndDate` range (when the event has one — `StoryEvent.StartDate`/
`EndDate` are already nullable) **or** the issue's `SeriesGroup`/`StoryArc` text contains the
event's `Name` (case-insensitive `Contains`, same idiom used everywhere else in this codebase for
this kind of loose text match). Requiring at least one of those two beyond Format alone keeps this
from surfacing every `Annual` in the whole library for every event — Format is a signal, not a
filter on its own. `Reason` is a short human-readable string built from whichever condition(s)
matched (e.g. `"Format: Annual · published 2015, within event range"`), shown directly in the
suggestion row rather than left implicit.

### UI: Suggested Issues

New section on `EventsScreenViewModel`'s detail pane, below Connected Events ([[phase4d-event-
relations]]): a collapsible "Suggested for this Event" list, each row showing the issue, its
`Reason` text, and an inline `Role` picker (pre-filled from `SuggestedRole` when the catalog
supplies one, otherwise defaulting the same way `SelectedRole` already defaults to `Core` elsewhere
on this screen) plus **Add** and **Dismiss** actions. Add calls the existing
`EventMembershipResolver.AddMember` with the (possibly user-changed) role and removes the row from
suggestions; Dismiss just removes the row for this session without persisting a "never suggest this
again" state — no dismissal-tracking table this phase, matching how lightweight the rest of this
feature's persistence footprint is.

### Format field editor + autocomplete

`Issue.Format` gets its first real editor — a combo-box-with-free-text-entry on the Issue
Properties / Bulk Issue Properties editors (matching every other CE-ported free-text field's
existing editor shape in those screens), seeded with `FormatSignalCatalog.Defaults.Keys` as the
autocomplete list (i.e. CE's 16 default values), same "type to search, or type something new"
pattern already used for continuity assignment ([[phase4a-continuity]]) and every other
combo-box-with-create in this codebase.

## Testing

- `FormatSignalCatalogTests`: every CE default value resolves to the documented strength/role; an
  arbitrary unrecognized string resolves to `None`; lookup is case-insensitive (`"annual"` matches
  `"Annual"`).
- `EventSuggestionResolverTests`: an issue with a strong-signal Format inside the event's date range
  is suggested with the correct `SuggestedRole`; the same issue outside the date range and with no
  matching `SeriesGroup`/`StoryArc` text is not suggested; an issue already an `EventMembership`
  member is excluded even if it would otherwise match; an event with no `StartDate`/`EndDate` falls
  back to text-match only.
- `EventsScreenViewModelTests`: Add moves a suggestion into the real member list with the (possibly
  edited) role; Dismiss removes it from the visible list without creating a membership row;
  reloading the event re-runs suggestions fresh (dismissals don't persist across a reload, as
  documented above).

## Explicitly out of scope

Auto-adding a suggestion without user confirmation — never, matches every other proposal-shaped
feature in this codebase. Persisting dismissals ("don't suggest this issue again") — a plausible
follow-up once real usage shows repeat suggestions are actually annoying, not built speculatively
now. Extending this suggestion logic to `MediaRelation` or `EventRelation` creation (only
`EventMembership` this phase). Any Format value beyond CE's 16 shipped defaults getting a built-in
signal classification — a user-typed custom Format value is always `FormatSignalStrength.None`
here; teaching the system to learn from custom values is real scope creep for a first pass.
