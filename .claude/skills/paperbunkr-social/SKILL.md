---
name: paperbunkr-social
description: The Paperbunkr posting pipeline for X (@Ehis_II) and Telegram - voice, per-platform formats, release announcements from CHANGELOG.md, and the draft -> approve -> publish steps (Buffer for X, the user's bot for Telegram). Use whenever writing, drafting, scheduling or publishing a Paperbunkr social post, tweet, thread or Telegram channel post, including release announcements and posts that go with a /brag video.
---

# Paperbunkr social posting

One pipeline, two platforms. Decided 2026-11-01:

| Setting | Value |
| --- | --- |
| X account | **@Ehis_II**. Connected in Buffer (channel id `6ae74910e29c69a17127e726`, org "My organization" `6a10525711bce82ec49de5a1`) |
| Telegram | **Default: the Updates topic (id 13) of the @Paperbunkr group**, which has Topics on and no channel of its own. The script targets it when no `-Channel` is given. **@nerd9ja_hub** (the user's Nerd9ja channel, `-Channel @nerd9ja_hub`) gets the fan series from `social/content-niches.md`, and Paperbunkr posts only when asked. Bot **@prictlybot** is an admin in both (checked 2026-11-01) |
| Publisher | **X: Buffer** (connected and verified 2026-11-01; `create_post` with `metadata.twitter.thread` for threads, `schedulingType: automatic`). **Telegram: the user's bot** through `social/post-telegram.ps1` (Buffer has no Telegram). No Posty: the posty.hu plugin was tried and dropped 2026-11-01 (the user meant posty.media, which has no Claude connection) |
| Writing | This file, plus the Social Media Skills plugin's writers (`thread-writer`, `hook-writer`, `text-post-and-microblog`) |
| Approval | **Every post is approved by the user before it goes out.** Nothing publishes automatically, releases included |
| Drafts | `social/drafts/YYYY-MM-DD-<slug>.md`, one file per post, both platforms inside |
| Captions | **`social/templates/captions.md`**: caption template per series (X, Telegram, poll). The caption never restates the card; it adds the hook, context and one ask. Each draft carries one alternative X caption under `## X alternatives` |
| Images | **`social/templates/`**: branded card layouts per series with image/clip slots; `node social/templates/render.js <spec.json>` → PNG, or GIF + MP4 for a clip. See its README. Clip-card titles reuse the brag edit's caption for that shot; every draft with media has an `alt:` line (pass it as Buffer's `altText`) |
| Daily posting | **`social/content-niches.md`**: 10 recurring series (comics, manga, manhwa, Paperbunkr), weekly rota, samples, Telegram versions. Fan series (1-8) go to X + **@nerd9ja_hub**; Paperbunkr series (9-10) to X + the Updates topic. Fan posts use the louder voice in that file; this file's voice rules are for Paperbunkr posts |

## The pipeline

1. **Source.** Either a release (that version's `## [x.y.z-beta]` section of `CHANGELOG.md`, and `gh release view vX.Y.Z-beta` to
   confirm it is actually published before saying "out now"), or a topic/clip the user gives you.
2. **Draft both versions from that one source** into a new file in `social/drafts/` (template below). Write them separately: X is
   not a cut-down Telegram post and Telegram is not a padded tweet.
3. **Show the user the draft** and get an explicit go-ahead for that post. Edits go into the draft file, then show it again.
4. **Publish** exactly the approved text, after confirming "post now or schedule?" with the user.
   - **X:** Buffer `create_post` on the @Ehis_II channel. A thread: `text` = post 1 and `metadata.twitter.thread` = all posts.
     **Video/images:** Buffer only takes a public URL. Upload the file to the repo's `media` **pre-release**
     (`gh release upload media <file>`; created 2026-11-01, user-approved) and pass
     `https://github.com/heisehis/PaperBunkr/releases/download/media/<file>` as the asset. Pre-releases are skipped by
     `releases/latest`, so the app's update check and the landing page never see it. Worked for a 32 MB mp4.
   - **Telegram:** `pwsh social/post-telegram.ps1 -Draft <file> -Check` first (sends nothing; confirms the bot can post), then
     the same with `-Send`. The default is @Paperbunkr's Updates topic; `-Channel @nerd9ja_hub` only when asked. A draft's
     `media:` line is sent as a video/photo with the text as caption (1024-char limit, 50 MB max). A 30 MB upload takes a few
     minutes per destination: run it in the background. The bot API has no scheduling: for a scheduled post,
     send it at the time, or tell the user.
5. **Record it.** Set the draft's `status:` to `posted` (or `scheduled: <time>`) and add the links Buffer and the script return.

## Voice

- Plain, concrete, confident. Say what it does, not how amazing it is. No "game-changer", "revolutionary", "excited to announce",
  "🚀 LFG". One emoji at most, and only if it carries meaning (📚 for a books feature is fine).
- Lead with what a reader can *do* now, not the internal name (say "read EPUBs with dictionary lookup and text-to-speech", not
  "Books reader ergonomics slice 4").
- **Never mention ComicRack**: not "successor to", not "imports your CE library", not "ComicRack-compatible". Standing rule from the
  user (same as the brag videos); they don't want that attention.
- Approved fact lines to reuse: **Free & open source** (AGPL-3.0, no paid tier) · **Local-first** (no cloud, no account, no
  telemetry) · **Reads it all** (CBZ · CBR · PDF · EPUB · FB2 · MOBI) · **Updates itself** (self-contained Windows installer).
- Windows only for now; don't imply Mac/Linux.
- Hashtags: none on Telegram; on X at most one, and only a real community tag (`#comics`, `#manga`) when the post is for that crowd.

## Links

- Release page: `https://github.com/heisehis/PaperBunkr/releases/tag/vX.Y.Z-beta` (latest: `.../releases/latest`)
- Landing site: `https://heisehis.github.io/PaperBunkr/`
- Repo: `https://github.com/heisehis/PaperBunkr`

## X (@Ehis_II)

- 280 characters per post; a link counts as 23. Check the count before showing the draft.
- Hook in the first line; the link goes in the **last** post of a thread (or a reply), since links in the opener cut reach.
- **Small release / single feature:** one post (headline + 1-2 concrete things + link).
- **Big release (3+ headline features):** a thread of 3-6 posts: opener with the version and the one biggest thing, one post per
  feature, last post = fact line + link. Number posts only if 5+.
- A /brag video goes on the opener of a release thread (or as its own post with a one-line caption + link).

## Telegram channel

- Up to 4096 characters as text, but **1024 if the post carries an image/video** (caption limit); keep release posts under ~900 so
  media can be added.
- Shape: bold-feeling first line (`Paperbunkr 0.8.2-beta is out`), one short intro sentence, a bullet list of the headline changes
  (`•`, one line each, from the changelog's **Added** headings, rewritten as plain benefits), one line for fixes/changes, then the
  download link on its own line.
- Plain text with line breaks and `•` bullets; the script sends plain text (no parse mode), so `*` or `_` show literally.
- Turn off link previews only if the post has its own media.

## Draft file template

```markdown
---
date: YYYY-MM-DD
source: CHANGELOG 0.8.2-beta | topic
status: draft            # draft | approved | scheduled: <time> | posted
media: none              # or a path, e.g. brag-output/paperbunkr-0.8.2.mp4
---

## X (@Ehis_II)

### 1/N (NNN chars)
...

## Telegram

...

## Telegram poll

Question on the first line
- Option one
- Option two

## Posted links
```

Both Telegram sections are optional; the script sends the post first, then the poll (2-10 options, anonymous).

## Buffer limits and the Telegram bot token

- **Buffer** is on the free plan: 3 channels (now @Ehis_II, Nerd9ja and a disconnected Instagram) and **10 scheduled posts** at a
  time. A thread counts as one post. "Post now" doesn't use a queue slot for long, but don't stack a month of posts in the queue.
- **The bot token** lives only in the Windows user environment variable `TELEGRAM_BOT_TOKEN`. The script reads it from there (no
  restart needed after it's set) and never prints it. Never ask for it in chat, pass it as an argument, or write it into a file.
  The user sets or replaces it in their own PowerShell window with:
  `[Environment]::SetEnvironmentVariable('TELEGRAM_BOT_TOKEN', (Read-Host 'Bot token' -MaskInput), 'User')`
- Fully automatic release posting from `.github/workflows/release.yml` was considered and **not** chosen (approval on every post;
  X's API needs a developer account and paid access).
