"""Picks the pages to label for the panel-model retraining project.

docs/superpowers/specs/2026-11-07-panel-model-retraining-design.md (Page sampler) and its -plan.md, Step 3.

Reads a COPY of the library database, picks issues spread across content type, publisher and decade, gives each issue to exactly
one split (test, batch1, batch2), and copies a few inner pages of each into DATA/<split>/images/. Writes DATA/manifest.csv.

Safety: archives are opened read-only; nothing is ever written next to the comics (the D: drive is failing). Only .cbz is read.

    py -I tools/panel-training/sample_pages.py --dry-run       # counts only, opens no archive
    py -I tools/panel-training/sample_pages.py                 # sample and copy
    py -I tools/panel-training/sample_pages.py --veto ID [ID ...]   # drop pages, draw replacements
"""

from __future__ import annotations

import argparse
import csv
import io
import os
import random
import shutil
import sqlite3
import sys
import zipfile
from collections import Counter, defaultdict
from dataclasses import dataclass

DEFAULT_DATA = r"C:\Users\DeeDee\PaperBunkr-panel-data"
DEFAULT_DB = os.path.join(os.environ.get("APPDATA", ""), "Paperbunkr", "paperbunkr.db")
IMAGE_EXT = (".jpg", ".jpeg", ".png", ".webp")
TALL_ASPECT = 2.4  # PanelDetectionService.TallAspect: taller than this many widths is a strip
MANIFEST_COLUMNS = ["id", "split", "issue_id", "source_path", "entry", "content_type", "publisher", "year",
                    "direction", "width", "height", "tall"]
RTL_MODES = {"1", "5", "RightToLeft", "HorizontalContinuousRightToLeft"}  # ReadingMode enum values 1 and 5
GROUPS = ("Western", "Manga", "Webtoon")


@dataclass
class Issue:
    id: int
    path: str
    content_type: str
    publisher: str
    year: int | None
    direction: str
    group: str
    stratum: str = ""


def group_of(content_type: str) -> str:
    if content_type == "Manga":
        return "Manga"
    if content_type in ("Manhwa", "Manhua"):
        return "Webtoon"
    return "Western"


def direction_of(override, series_mode, content_type: str) -> str:
    for mode in (override, series_mode):
        if mode is not None and str(mode) != "":
            return "rtl" if str(mode) in RTL_MODES else "ltr"
    return "rtl" if content_type == "Manga" else "ltr"


def load_issues(db_copy: str) -> list[Issue]:
    con = sqlite3.connect(f"file:{db_copy}?mode=ro", uri=True)
    rows = con.execute(
        """select i.Id, i.FilePath, coalesce(s.ContentType, 'Unknown'), coalesce(i.Publisher, ''), i.Year,
                  i.ReadingModeOverride, s.ReadingMode
           from Issues i left join Series s on s.Id = i.SeriesId
           where lower(i.FilePath) like '%.cbz'""").fetchall()
    con.close()
    issues = []
    for iid, path, ctype, publisher, year, override, series_mode in rows:
        year = year if isinstance(year, int) and year > 1900 else None
        issues.append(Issue(iid, path, ctype, publisher.strip() or "(none)", year,
                            direction_of(override, series_mode, ctype), group_of(ctype)))
    return issues


def assign_strata(issues: list[Issue]) -> None:
    """Stratum = publisher (top 8 in its group, else 'other') x decade."""
    for group in GROUPS:
        members = [i for i in issues if i.group == group]
        top = {p for p, _ in Counter(i.publisher for i in members).most_common(8)}
        for i in members:
            decade = f"{i.year // 10 * 10}s" if i.year else "year?"
            i.stratum = f"{i.publisher if i.publisher in top else 'other'}|{decade}"


class Drawer:
    """Hands out issues of one group round-robin across its strata, never the same issue twice."""

    def __init__(self, issues: list[Issue], rng: random.Random):
        by_stratum: dict[str, list[Issue]] = defaultdict(list)
        for i in issues:
            by_stratum[i.stratum].append(i)
        self.queues = []
        for key in sorted(by_stratum):
            q = by_stratum[key][:]
            rng.shuffle(q)
            self.queues.append(q)
        rng.shuffle(self.queues)
        self.turn = 0

    def next(self, stratum: str | None = None) -> Issue | None:
        if stratum is not None:
            for q in self.queues:
                if q and q[0].stratum == stratum:
                    return q.pop(0)
            # stratum exhausted: fall back to any stratum
        for _ in range(len(self.queues)):
            q = self.queues[self.turn % len(self.queues)]
            self.turn += 1
            if q:
                return q.pop(0)
        return None


def plan_counts(batch_size: int, test_counts: dict[str, int]) -> dict[str, dict[str, int]]:
    """Pages per split and group. Batches follow the library's mix, with manga and webtoon raised to at least 20% each."""
    batch = {"Manga": round(batch_size * 0.20), "Webtoon": round(batch_size * 0.25)}
    batch["Western"] = batch_size - batch["Manga"] - batch["Webtoon"]
    return {"test": dict(test_counts), "batch1": dict(batch), "batch2": dict(batch)}


def inner_entries(path: str) -> list[str]:
    with zipfile.ZipFile(path) as z:
        names = sorted((e.filename for e in z.infolist() if e.file_size > 0 and e.filename.lower().endswith(IMAGE_EXT)),
                       key=str.lower)
    if len(names) < 6:
        return []
    return names[2:-2]  # skip covers and back-matter, like the harness


def copy_pages(issue: Issue, split: str, k: int, data: str, rng: random.Random, exclude: set[str]) -> list[dict]:
    entries = inner_entries(issue.path)
    if not entries:
        raise ValueError("fewer than 6 image pages")
    picks = sorted(rng.sample(range(len(entries)), min(k, len(entries))))
    out_dir = os.path.join(data, split, "images")
    os.makedirs(out_dir, exist_ok=True)
    rows = []
    from PIL import Image  # imported late so --dry-run works without Pillow
    with zipfile.ZipFile(issue.path) as z:
        for index in picks:
            entry = entries[index]
            page_id = f"i{issue.id}p{index + 2:04d}"  # index within the archive's sorted image list
            if page_id in exclude:
                continue
            blob = z.read(entry)
            with Image.open(io.BytesIO(blob)) as im:
                width, height = im.size
            ext = os.path.splitext(entry)[1].lower()
            with open(os.path.join(out_dir, page_id + ext), "wb") as f:
                f.write(blob)
            rows.append({"id": page_id, "split": split, "issue_id": issue.id, "source_path": issue.path, "entry": entry,
                         "content_type": issue.content_type, "publisher": issue.publisher, "year": issue.year or "",
                         "direction": issue.direction, "width": width, "height": height,
                         "tall": height > TALL_ASPECT * width})
    return rows


def write_manifest(path: str, rows: list[dict]) -> None:
    with open(path, "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=MANIFEST_COLUMNS)
        w.writeheader()
        w.writerows(rows)


def read_manifest(path: str) -> list[dict]:
    with open(path, newline="", encoding="utf-8") as f:
        return list(csv.DictReader(f))


def copy_db(db: str, data: str) -> str:
    os.makedirs(data, exist_ok=True)
    target = os.path.join(data, "lib.db")
    # SQLite's backup API, not a file copy: Paperbunkr may be running, and a file copy can catch the database (and its -wal file) mid-write.
    # The live file is opened read-only; only the copy is used afterwards.
    src = sqlite3.connect(f"file:{db}?mode=ro", uri=True)
    dst = sqlite3.connect(target)
    try:
        src.backup(dst)
    finally:
        dst.close()
        src.close()
    return target


def sample(args, log) -> int:
    manifest_path = os.path.join(args.data, "manifest.csv")
    if os.path.exists(manifest_path) and not args.dry_run and not args.force:
        print(f"{manifest_path} already exists; labels may depend on it. Use --veto to change pages, or --force to start over.")
        return 1
    issues = load_issues(copy_db(args.db, args.data))
    assign_strata(issues)
    rng = random.Random(args.seed)
    test_counts = dict(zip(GROUPS, (int(x) for x in args.test_counts.split(","))))
    counts = plan_counts(args.batch_size, test_counts)
    drawers = {g: Drawer([i for i in issues if i.group == g], rng) for g in GROUPS}
    print(f"{len(issues)} .cbz issues: " + ", ".join(f"{g} {sum(1 for i in issues if i.group == g)}" for g in GROUPS))

    rows: list[dict] = []
    for split in ("test", "batch1", "batch2"):
        for group in GROUPS:
            need = counts[split][group]
            got = 0
            strata = Counter()
            while got < need:
                issue = drawers[group].next()
                if issue is None:
                    print(f"  {split}/{group}: ran out of issues at {got} of {need} pages")
                    break
                k = min(args.pages_per_issue, need - got)
                if args.dry_run:
                    got += k
                    strata[issue.stratum] += 1
                    continue
                try:
                    new = copy_pages(issue, split, k, args.data, random.Random(f"{args.seed}-{issue.id}"), set())
                except (zipfile.BadZipFile, OSError, ValueError, KeyError) as ex:
                    log.write(f"skip issue {issue.id} ({issue.path}): {ex}\n")
                    continue  # the next draw replaces it from the same group, next stratum in turn
                rows.extend(new)
                got += len(new)
                strata[issue.stratum] += 1
            print(f"  {split:7} {group:8} {got:4} pages from {sum(strata.values()):3} issues, {len(strata)} strata")

    if args.dry_run:
        print("dry run: no archive opened, nothing copied")
        return 0
    write_manifest(manifest_path, rows)
    print(f"{len(rows)} pages -> {args.data}; manifest {manifest_path}; skipped archives are listed in sample.log")
    return check(args.data, rows)


def check(data: str, rows: list[dict]) -> int:
    """The Step 3 checks: every manifest row has its image, no issue is in two splits, and the folder stays small."""
    problems = 0
    splits_by_issue: dict[str, set[str]] = defaultdict(set)
    size = 0
    for r in rows:
        splits_by_issue[str(r["issue_id"])].add(r["split"])
        folder = os.path.join(data, r["split"], "images")
        files = [f for f in os.listdir(folder) if os.path.splitext(f)[0] == r["id"]]
        if not files:
            print(f"missing image for {r['id']}")
            problems += 1
        else:
            size += os.path.getsize(os.path.join(folder, files[0]))
    shared = [i for i, s in splits_by_issue.items() if len(s) > 1]
    if shared:
        print(f"issues in more than one split: {shared[:10]}")
        problems += 1
    print(f"images total {size / 1e9:.2f} GB")
    if size > 1.5e9:
        print("over the 1.5 GB budget")
        problems += 1
    return 0 if problems == 0 else 1


def veto(args, log) -> int:
    manifest_path = os.path.join(args.data, "manifest.csv")
    rows = read_manifest(manifest_path)
    db_copy = os.path.join(args.data, "lib.db")
    issues = load_issues(db_copy)
    assign_strata(issues)
    by_id = {i.id: i for i in issues}
    used_issues = {int(r["issue_id"]) for r in rows}
    vetoed_dir = os.path.join(args.data, "vetoed")
    os.makedirs(vetoed_dir, exist_ok=True)
    for page_id in args.veto:
        row = next((r for r in rows if r["id"] == page_id), None)
        if row is None:
            print(f"{page_id}: not in the manifest")
            continue
        rows.remove(row)
        images = os.path.join(args.data, row["split"], "images")
        # Moved aside, not deleted, so a mistaken veto can be undone by hand.
        for f in os.listdir(images):
            if os.path.splitext(f)[0] == page_id:
                shutil.move(os.path.join(images, f), os.path.join(vetoed_dir, f))
        label = os.path.join(images, "labels", page_id + ".txt")
        if os.path.exists(label):
            shutil.move(label, os.path.join(vetoed_dir, page_id + ".txt"))
        source = by_id.get(int(row["issue_id"]))
        group = source.group if source else group_of(row["content_type"])
        stratum = source.stratum if source else None
        drawer = Drawer([i for i in issues if i.group == group and i.id not in used_issues], random.Random(f"{args.seed}-veto-{page_id}"))
        replaced = False
        while not replaced:
            issue = drawer.next(stratum)
            if issue is None:
                print(f"{page_id}: vetoed, no replacement left in {group}")
                break
            try:
                new = copy_pages(issue, row["split"], 1, args.data, random.Random(f"{args.seed}-{issue.id}"), set())
            except (zipfile.BadZipFile, OSError, ValueError, KeyError) as ex:
                log.write(f"skip issue {issue.id} ({issue.path}): {ex}\n")
                used_issues.add(issue.id)
                continue
            rows.extend(new)
            used_issues.add(issue.id)
            print(f"{page_id}: vetoed, replaced by {new[0]['id']} ({issue.stratum})")
            replaced = True
    write_manifest(manifest_path, rows)
    return check(args.data, rows)


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--data", default=DEFAULT_DATA)
    p.add_argument("--db", default=DEFAULT_DB, help="the live library DB; it is copied to DATA/lib.db and only the copy is read")
    p.add_argument("--seed", type=int, default=1)
    p.add_argument("--test-counts", default="90,30,30", help="test pages: Western,Manga,Webtoon")
    p.add_argument("--batch-size", type=int, default=300)
    p.add_argument("--pages-per-issue", type=int, default=3)
    p.add_argument("--dry-run", action="store_true")
    p.add_argument("--force", action="store_true", help="re-sample even though a manifest exists")
    p.add_argument("--veto", nargs="+", metavar="ID")
    args = p.parse_args()
    if args.data.upper().startswith("D:"):
        print("refusing to write to D: (the drive is failing)")
        return 1
    if not args.veto and not os.path.exists(args.db):
        print(f"no database at {args.db}")
        return 1
    os.makedirs(args.data, exist_ok=True)
    with open(os.path.join(args.data, "sample.log"), "a", encoding="utf-8") as log:
        return veto(args, log) if args.veto else sample(args, log)


if __name__ == "__main__":
    sys.exit(main())
