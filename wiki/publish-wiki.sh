#!/usr/bin/env bash
# Publish wiki/*.md to the GitHub wiki repo for heisehis/PaperBunkr.
# Prereq (one time): create the first wiki page via the web UI so the
# PaperBunkr.wiki.git repo exists. See wiki/_PUBLISHING.md.
set -euo pipefail

WIKI_REMOTE="https://github.com/heisehis/PaperBunkr.wiki.git"
SRC_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT

echo "Cloning $WIKI_REMOTE ..."
if ! git clone --quiet "$WIKI_REMOTE" "$WORK_DIR/wiki"; then
  echo
  echo "ERROR: could not clone the wiki repo."
  echo "If this is the first time, open https://github.com/heisehis/PaperBunkr/wiki"
  echo "and create the first page in the browser, then re-run this script."
  exit 1
fi

cd "$WORK_DIR/wiki"

# Copy every page except the publishing helpers.
for f in "$SRC_DIR"/*.md; do
  base="$(basename "$f")"
  case "$base" in
    _PUBLISHING*) continue ;;
  esac
  cp "$f" "./$base"
done

if git diff --quiet && git diff --cached --quiet; then
  echo "No changes to publish."
  exit 0
fi

git add -A
git -c user.name="heisehis" -c user.email="ibhiabore@gmail.com" \
    commit --quiet -m "Update wiki from repo wiki/ folder"
git push --quiet origin HEAD
echo "Published. See https://github.com/heisehis/PaperBunkr/wiki"
