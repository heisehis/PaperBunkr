# Reading List automatic tags

**Date:** 2026-10-15
**Status:** Approved and implemented
**Phase:** 1 of the ordered Reading Lists / Continuity improvements

## Goal

Automatically label Reading Lists from their own metadata and the comics they contain, while keeping user-entered tags safe and visibly distinct from generated tags.

## Repository and ComicRack findings

- `ReadingList` already has `Source`, `Type`, `StoryEventId`, `ContinuityId`, and a `Tags` collection.
- `ReadingListTag` already stores `Value`, `Category`, and `IssueTagWeight`, but has no provenance field.
- Each `Issue` has categorized `IssueTag` rows, with separate `Genre` and `Tags` fields, a free-text category, and a weight.
- ComicRack CE's `ComicIdListItem` stores an ordered set of book IDs; it has no Reading List tag model. CBL Manager's arc-source metadata is separate from CE's list model. Automatic tags are a deliberate PaperBunkr extension, not CE parity.
- Existing Reading List tags are displayed and used by the existing list-tag filtering behavior. Generated tags should participate in the same filtering.

## Decisions

1. Generate tags from four Reading List properties: source, list type, linked Story Event, and linked Continuity.
2. Aggregate both Genre and Tags values from member issues. Deduplicate repeated values for the same category, retain the issue tag's category, and do not infer a weight.
3. Keep generated and user-created tags separately identifiable. Reconciliation may update or remove generated tags but must preserve user-created values, categories, weights, and edits.
4. Refresh generated tags when relevant list metadata, membership, or a member issue's tags change. Use the same reconciliation logic for source imports and list builds.
5. Run a one-time, local backfill for existing Reading Lists after upgrade, using stored metadata and member-issue tags. No network access is needed for this pass.
6. Display generated tags in the existing chip area with a subtle generated marker; user tags retain the existing presentation. Generated tags participate in current tag filtering.

## Design

### Generated tag identity and reconciliation

Add provenance to `ReadingListTag` sufficient to distinguish a manually curated tag from an app-managed tag and identify the rule that generated it. Generated values use stable, normalized identities so a refresh updates existing generated rows rather than duplicating them. Manual rows are never deleted or rewritten by reconciliation. When a manual tag already represents the same value/category, it satisfies the visible tag and is preserved as the authoritative row.

The desired generated set is calculated from current source/type/event/continuity fields and the distinct tags on current member issues. A reconciliation operation diffs that set against existing generated rows and applies the changes transactionally. Issue-derived values retain their issue category; the source field is retained in provenance rather than inferred as a user-facing weight. Metadata-derived values use consistent categories (`Source`, `List type`, `Story Event`, `Continuity`) and `Unset` weight.

### Update triggers and backfill

Reconcile after a Reading List's source, type, event, or continuity changes; after item add/remove/relink or a complete list build/import; and after an issue's tags are edited for lists containing that issue. Batch affected list IDs and avoid rescanning unrelated lists. A startup migration/backfill pass is resumable and idempotent; it reads the local database only and reports failures without blocking normal library startup.

### UI

Reuse the current Reading List tag chip surface and tag filtering. Generated chips receive a restrained marker and explanatory tooltip/label. User tags retain existing editing and weighting behavior. Generated tags are maintained from source metadata and member comics; the properties editor must not silently turn a generated tag into a user tag or allow edits that are overwritten on the next refresh. If a user wants a lasting custom form of a generated tag, adding a separate user tag remains available.

## Data migration

Add nullable/defaulted provenance fields to `ReadingListTag` with existing rows backfilled as user-created. Run the generated-tag backfill after schema migration, in bounded idempotent batches. Existing `ReadingListTag` category and weight values are preserved. No provider IDs are added, and no list membership or order is modified.

## Failure and integrity behavior

- Reconciliation runs in a transaction per bounded batch/list so a failure cannot leave a list with a partial generated set.
- A missing or removed source association simply removes that source-generated tag on the next reconciliation; user tags remain.
- A member issue without tags contributes nothing.
- Case-insensitive normalized duplicates collapse, while distinct categories remain separate.
- Auto-tagging never changes an issue's own tags or weights.

## Testing

- Four metadata sources produce the expected categorized tags; null/unknown metadata produces none.
- Genre and Tags on member issues are both included, categories are retained, duplicates collapse, and weights remain unset.
- Add/remove/relink of members updates only generated tags; manual tags retain category/weight.
- An issue tag edit refreshes only lists containing that issue.
- Existing-list backfill is idempotent, bounded, resumable, and preserves all existing manual tags.
- Tag filtering finds lists through both manual and generated tags.
- Database failures roll back the affected reconciliation batch.

## Out of scope

Source logos; Story Event tags and cover art; provider refresh of event descriptions; changes to member comic metadata; changes to Reading List ordering or the Reading Lists page layout.

