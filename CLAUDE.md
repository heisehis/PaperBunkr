# Paperbunkr

ComicRack-inspired comic/manga library and reader, Avalonia/.NET 8. Full design doc:
[docs/onboarding.md](docs/onboarding.md). CE parity audit: [docs/ce-feature-inventory.md](docs/ce-feature-inventory.md).

## Roadmap status — check this first in a new session

- **Source of truth:** [docs/alpha-todo.md](docs/alpha-todo.md) — P0–P7 alpha checklist plus the
  unsequenced Beta backlog, with commit refs and rationale for every status.
- **Live dashboard (lighter view, same data):**
  https://claude.ai/code/artifact/0ca86894-977e-45e2-951b-476e1150a5ee
- **Kept in sync by:** a scheduled cloud routine (`paperbunkr-alpha-tracker-sync`, every 6h,
  read-only) that diffs `git log` against the dashboard's own embedded `HEAD` marker and
  republishes only verified status changes. It never edits `docs/alpha-todo.md` or commits
  anything — manage/inspect it at https://claude.ai/code/routines.
- **If you land roadmap-relevant work in a session:** update `docs/alpha-todo.md` by hand (status,
  commit ref, what you verified — not just what the commit message claims). The dashboard's own
  6-hourly check will pick up the underlying commits regardless, but the written doc is what a
  human actually reads for the "why."
- The doc drifted out of sync with the repo once already (see its "Live tracker" section) —
  don't assume either the doc or the dashboard is current without checking `git log` against the
  `HEAD` hash each one records.

## Standing rule

Before adding any field, default, or behavior, verify it against the original ComicRack CE
source/behavior (`_reference/ComicRackCE`) rather than assuming — this project is a from-scratch
rewrite aiming for CE parity plus deliberate deviations, not a guess at what CE probably did.
