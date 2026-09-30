# Continuity map + smart connector — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-27-continuity-map-design.md*

Live Wikidata check, read-only, 2026-09-27:
- **Planet Hulk** (Q2526264): P31 = Q47461344 ("written work"); P123 = Q173496 (Marvel Comics); P156 = World War Hulk
  (Q1048144) and Q154242 ("nothing"); P155 = Q154242.
- **World War Hulk** (Q1048144): P31 = Q3297186 ("limited series"); P155 = Q2526264.
- **What that means for matching:** items of the same kind are typed inconsistently. The type check is an allow-list, not a
  single class: comic book storyline Q115378877, limited series Q3297186, comic book series Q14406742, written work Q47461344.
  Precision comes from the name keys plus a publisher check. The publisher's label is fetched once per run and compared after
  `NormalizePublisher`. A ComicVine arc id match skips the type check.

## Step 1: Schema
**Files:**
- `RelationEvidenceProvider` (+`Inferred`, `Wikidata`)
- `ArcVerificationSource` (+`Wikidata`)
- `StoryEvent` (+`WikidataQid`, `ChronologyCheckedAt`)
- `EventRelationDismissal` (new)
- DbContext
- migration `AddEventChronology` + a forward-only test

## Step 2: Chronology and inference (Data, local)
**Files:**
- `EventChronology.cs`: relation direction helpers, `EventSpan`, `Order`
- `EventChronologyInference.cs`
- tests

## Step 3: Wikidata links
**Files:**
- `WikidataClient.cs`: P155/P156/P5905 on `WikidataEntity`; `FindByComicVineIdAsync` (the `haswbstatement` search); an
  `IWikidataLookup` seam
- `WikidataEventLinks.cs`
- tests with a fake lookup

## Step 4: Connector sweep
**Files:**
- `EventConnectorSweep.cs`: persist strong inferences, delete vanished ones, dismissals
- `EventRelationSuggestionResolver.cs`: typed weak suggestions first
- scheduled task renamed "Check story events", which runs identity then connector
- tests

## Step 5: Continuity map loader and layout
**Files:**
- `Services/EventMap/ContinuityMapLoader.cs`: blocks, lanes, connectors, counts
- `EventMapModels.cs`: block, connector and per-card extras
- `EventMapLayout.cs`: `ComputeBlocks`, the block-relay mode
- tests

## Step 6: App
**Files:**
- `EventMapViewModel`: continuity scope, Events picker, Events only, band click, also-in, reader anchor per card
- `EventsScreenViewModel.Map.cs`: Map allowed for continuities
- `EventMapRuler`: band row, connectors, hit test
- `EventMapView.axaml`: toolbar variants, inspector additions
- `EventsScreen.axaml`: the toggle for continuities
- Related events panel: typed suggestions, dismissal on delete
- button rename
- App tests

## Step 7: Docs and verification
Wiki, todo, design implementation notes; targeted App and Data test runs.
