---
date: 2026-11-04
series: 9 Build Log (Paperbunkr) - guided view panel model (CHANGELOG 0.7.5: ~93% of pages on a test library, up from 14%) - goal: clicks
schedule: 2026-11-04 15:00 WAT
status: posted           # X: Buffer 15:00 WAT; Telegram: daily task 15:00 WAT (approved 2026-11-03)
media: social/media/2026-11-04-guided-view.gif     # card: social/templates/specs/2026-11-04-guided-view.json (C3 take 2, 0:11-0:20)
alt: Screen recording: Paperbunkr's guided view stepping through a comic page panel by panel. Title: Build log, Guided view, panel by panel.
telegram: @Paperbunkr
---

## X (@Ehis_II)

### 1/1
Guided view used to find panels on 14% of pages in my test library.

Now a small panel-detection model runs first: 93%.

Press G while reading and Paperbunkr walks the page panel by panel.

https://github.com/heisehis/PaperBunkr/releases/latest

## Telegram

From 14% to 93%.

That's how often guided view finds the panels on a page in my test library, before and after a small panel-detection model started running first.

• Press G while reading (single-page mode) to turn it on
• Next key, Space or a click steps through the panels
• A page it gets wrong? Command palette → Report bad panel detection

Download: https://github.com/heisehis/PaperBunkr/releases/latest

## X alternatives

Comic pages don't come with panel coordinates, so guided view has to guess. It used to get 14% of my test pages. With a small model running first: 93%.

https://github.com/heisehis/PaperBunkr/releases/latest

## Posted links

- X @Ehis_II: scheduled in Buffer for 15:00 WAT (post id 6ae9b494de49dc57f0118b88); link appears once it posts
- Telegram @Paperbunkr: https://t.me/Paperbunkr/13/20
