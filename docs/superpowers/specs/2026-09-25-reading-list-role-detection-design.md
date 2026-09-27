# Automatic role detection for reading-list and event members — design

*Status: ~~draft for user review, 2026-09-25.~~ **Built, uncommitted** — confirmed 2026-09-26 via
source: `MemberRoleDetector` (`RoleConfidence`/`RoleSuggestion`/`MemberRoleFacts`) and
`MemberRoleApplier` present and untracked, matching the plan's Steps 1 and 3. On-screen check by the
user still outstanding. Grilled with the user the same day; every choice below was either
recommended-and-accepted or explicitly decided (six roles kept; "Aftermath" in a comic's name is a signal).*

## Goal

Suggest — and, when the evidence is strong, apply — an `EventMembershipRole`
(`Prologue, Core, TieIn, Epilogue, Optional, Aftermath`) to issues in reading lists and story events, from data
Paperbunkr already has or can cheaply fetch, without ever overwriting a role the user chose.

## Facts this design rests on (verified in the repo)

- The only role concept is `EventMembershipRole`. `EventMembership.Role` is non-nullable; `ReadingListItem.Role` is
  nullable. Nothing in `ReadingOrderResolver` uses roles; they are displayed, edited and copied only.
- Roles are assigned by hand today. The one automatic path is `FormatSignalCatalog` (Format `Prologue`/`Minus 1` →
  Prologue, `Epilogue` → Epilogue), consumed by `EventSuggestionResolver`, which proposes *issues*, not roles.
- Arc lookup (`ArcReadingListBuilder`, six sources) creates items with only `IssueId` and `SortOrder`. All sources
  return the same `ArcIssue(Series, Number, Year, CoverImageUrl)`; none carries a role. Comic Book Reading Orders'
  parser currently *discards* the blue annotation spans and headings (`ComicBookReadingOrdersSource.cs` ~69-72),
  the only place role hints exist in any source.
- ComicVine and Metron expose no role on arc membership. AniList/MangaBaka relation types are manga series-level.
  Wikidata reaches continuity level only. None of these are used for roles here.
- ComicRack CE has no role concept, so there is no CE parity to preserve; this is a Paperbunkr-only feature.

## Non-goals

- New roles, or renaming the six. `Optional` is never suggested (only the user sets it).
- Wikidata / Fandom / AniList issue-level role lookup (nothing there carries per-issue roles).
- Changing `ReadingOrderResolver` ordering by role.

## Design

### 1. Storage (additive migration on `EventMemberships` and `ReadingListItems`)

| Column | Type | Meaning |
|---|---|---|
| `RoleSource` | nullable enum (`User`, `Auto`) stored as string ≤32 | Who set `Role`. Null = existing/legacy row, treated as `User`. |
| `RoleReason` | nullable string ≤200 | Why it was set, e.g. `Format: Prologue`. |
| `SuggestedRole` | nullable `EventMembershipRole` (string ≤32) | A pending low-confidence suggestion. |
| `SuggestedReason` | nullable string ≤200 | Reason for the pending suggestion. |
| `RoleSuggestionDismissed` | bool, default false | User rejected the suggestion; do not re-suggest. |

(The plan may instead reuse the dismissal-table pattern of `EventSuggestionDismissal` if it fits better; the
column is the simpler default. Decide in the plan after reading that entity.)
`EventMembership.Role` stays non-nullable.

### 2. Detector — `MemberRoleDetector` (pure, in `Paperbunkr.Data`)

Input: issue facts (Format, issue title, series name, number, StoryArc), arc context (arc name, member position and
total, the dominant series among members, and any source annotation/heading). Output: `RoleSuggestion(Role, Confidence,
Reason)` or `null`. Whole-word, case-insensitive matching.

| Signal | Suggests | Confidence |
|---|---|---|
| Source annotation or section heading: tie-in / prelude / prologue / epilogue / aftermath | matching role | High |
| Format field: Prologue, Minus 1, Epilogue (existing `FormatSignalCatalog`) | Prologue / Epilogue | High |
| Issue title contains prologue, prelude, or number is 0 / -1 | Prologue | High |
| Issue title contains epilogue | Epilogue | High |
| Issue title contains **aftermath** | Aftermath | High |
| Issue title contains "tie-in" / "tie in" | TieIn | High |
| The same words appear only in the *series name* | matching role | Low |
| Issue's series differs from the arc's dominant series | TieIn | Low |
| As above, and it is the first / last member of an arc with ≥ 6 issues | Prologue / Epilogue | Low |

Rationale for High on an issue-title match: the issue is already a member of an arc or event, so an unrelated
title is unlikely. A series-name match is Low because unrelated series named "Aftermath" exist. "Dominant series"
is computed from the members themselves (most frequent series name) — no extra API calls.
Precedence: annotation > Format > issue title > series name > structural. Never returns `Optional`.

### 3. Applying

- **High**: written into `Role` with `RoleSource = Auto` and `RoleReason`, but only into an *empty or Auto slot*:
  a `ReadingListItem` whose `Role` is null, or any row with `RoleSource = Auto`. Newly created members
  (arc-list build, event suggestion accept) count as empty.
- **Low**, or High where the slot is `User`/legacy: never overwrites; stored in `SuggestedRole`/`SuggestedReason` when it
  differs from the current role and `RoleSuggestionDismissed` is false.
- A role with `RoleSource = User` (or null) is never changed by detection. Accepting a suggestion is a user action →
  `RoleSource = User`.

### 4. Where it runs

1. `ArcReadingListBuilder.CreateFromArcAsync` (after items are created) and `RefreshAsync` (items without a user role).
2. Event-suggestion accept paths that currently hard-code `Core` (`EventsScreenViewModel.StoryEventSuggestions.cs:57`,
   `IssuePropertiesScreenViewModel.cs:269`) use the detector's High result with `RoleSource = Auto`; when the detector
   returns nothing or Low they keep today's behaviour (`Core`, `RoleSource = User`), and a Low result is stored as a suggestion.
3. A new **Detect roles** action on an event and on a reading list.

### 5. Source-parser change

`ComicBookReadingOrdersSource` keeps the annotation spans and headings and returns them on `ArcIssue` as a new optional
`Annotation` string (record gains a nullable field; the other five sources leave it null). The builder passes it to the
detector. Tests use the existing parsing fixtures.

### 6. UI

- An "auto" marker beside a detected role in the event member rows and reading-list item rows, with a tooltip showing
  `RoleReason`; one-click **Clear** (sets the role back to empty for list items; for events, reverts to `Core` with
  `RoleSource = User`).
- Pending suggestions show a small "Suggested: Tie-in — <reason>" chip with **Accept** / **Dismiss**.
- Uses `PbRadiusChip` squircle styling, no oval pills. Load the `avalonia` skill and run the review checklist before calling UI done.
- Activity Center: one job per **Detect roles** run and per builder run; summary like "12 roles detected, 5 need review".
  Toasts follow the existing Activity Center suppression rules.

## Testing

- `MemberRoleDetectorTests`: every table row, precedence, whole-word matching ("Aftermath" vs "Aftermathematics"),
  and cases that must return null.
- Builder/refresh tests: High auto-applies to empty slots; a `User` role survives create, refresh and Detect;
  Low lands in `SuggestedRole`; dismissed suggestions are not re-suggested.
- Migration test (additive columns, legacy rows read back as `User`).
- Parser test: annotations/headings retained from the Comic Book Reading Orders fixture.
- View-model tests for Accept / Dismiss / Clear. On-screen verification is the user's.

## Docs to update when built

`docs/paperbunkr-todo.md` (status, evidence), `docs/onboarding.md` metadata section, the events/reading-list wiki pages.

## Risks

- Heuristics can be wrong; mitigated by the High/Low split, the never-overwrite rule and the review chip.
- The first/last rule may be noisy in practice; it is one table row and can be dropped.
