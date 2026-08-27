# Publishing this wiki to GitHub

These `.md` files are the source for the GitHub wiki at
<https://github.com/heisehis/PaperBunkr/wiki>. They are **not** part of the main repo's
history — GitHub stores wiki content in a separate git repo,
`https://github.com/heisehis/PaperBunkr.wiki.git`.

## One-time: create the wiki repo

GitHub won't create `PaperBunkr.wiki.git` until the **first page exists**. Do this once:

1. Go to <https://github.com/heisehis/PaperBunkr/wiki>.
2. Click **Create the first page**.
3. Type anything, click **Save Page**.

That's it — the wiki git repo now exists and the script below can take over.

## Publish / update

From the repo root, run:

```bash
bash wiki/publish-wiki.sh
```

It clones `PaperBunkr.wiki.git` into a temp dir, copies every `*.md` from `wiki/` (except
files starting with `_PUBLISHING`), commits, and pushes. Re-run it whenever you edit a
page here.

## File → page mapping

- `Home.md` → wiki landing page
- `_Sidebar.md` → the navigation sidebar (GitHub renders this specially)
- `Foo-Bar.md` → a page titled "Foo Bar"; link to it as `[[Foo Bar]]` or `[text](Foo-Bar)`

## Editing directly on GitHub instead

You can also edit pages in the GitHub wiki UI. If you do, pull those changes back into
`wiki/` here (the script does not) so this copy stays authoritative:

```bash
git clone https://github.com/heisehis/PaperBunkr.wiki.git /tmp/pbwiki && cp /tmp/pbwiki/*.md wiki/
```
