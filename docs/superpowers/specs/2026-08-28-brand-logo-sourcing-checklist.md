# Brand logo sourcing checklist

Companion to `2026-08-28-brand-metadata-iconography-design.md`. The user sources these SVGs; the
`MarkResolver` picks them up by exact filename. Anything not supplied falls back to a coloured
letter-mark — so everything here is best-effort, nothing blocks the build.

Format: **SVG**, one file each. Prefer clean vector (not an auto-traced raster). Monochrome is fine —
the resolver / `BrandMark` can tint; colour logos are used as-is.

## Services → `src/Paperbunkr.App/Assets/Marks/Services/`

| Filename | Service | Source | Priority |
|---|---|---|---|
| `anilist.svg` | AniList | simpleicons.org `anilist` | get it |
| `myanimelist.svg` | MyAnimeList | simpleicons.org `myanimelist` | get it |
| `kitsu.svg` | Kitsu | simpleicons.org `kitsu` | get it |
| `bangumi.svg` | Bangumi | simpleicons.org `bangumi` | get it |
| `mangadex.svg` | MangaDex | mangadex.org media kit | nice to have |
| `comicvine.svg` | Comic Vine | Wikimedia Commons | nice to have |
| `mangabaka.svg` | MangaBaka | mangabaka.dev header/favicon | nice to have |
| `mangaupdates.svg` | MangaUpdates | mangaupdates.com footer | nice to have |
| `metron.svg` | Metron | metron.cloud | nice to have |
| `animeplanet.svg` | Anime-Planet | anime-planet.com | letter-mark OK (`AP`) |
| `shikimori.svg` | Shikimori | shikimori.one | letter-mark OK (`SK`) |
| `gcd.svg` | Grand Comics Database | comics.org | letter-mark OK (`GCD`) |
| `locg.svg` | League of Comic Geeks | leagueofcomicgeeks.com | letter-mark OK (`LOCG`) |
| `cbro.svg` | Comic Book Reading Orders | comicbookreadingorders.com | letter-mark OK (`CBRO`) |
| `readingorders.svg` | ReadingOrders.net | readingorders.net | letter-mark OK (`RON`) |
| `readthingsright.svg` | Read Things Right | readthingsright.com | letter-mark OK (`RTR`) |

## Publishers → `src/Paperbunkr.App/Assets/Marks/Publishers/`

All optional — coloured letter-mark is the fallback.

### Western
| Filename | Publisher | Source |
|---|---|---|
| `marvel.svg` | Marvel Comics | Wikimedia Commons |
| `dc.svg` | DC Comics | Wikimedia Commons |
| `image.svg` | Image Comics | imagecomics.com |
| `dark-horse.svg` | Dark Horse Comics | darkhorse.com |
| `idw.svg` | IDW Publishing | idwpublishing.com |
| `viz.svg` | VIZ Media | viz.com |
| `boom.svg` | BOOM! Studios | boom-studios.com |
| `dynamite.svg` | Dynamite Entertainment | dynamite.com |
| `valiant.svg` | Valiant Entertainment | valiantentertainment.com |
| `titan.svg` | Titan Comics | titan-comics.com |
| `oni.svg` | Oni Press | onipress.com |
| `dstlry.svg` | DSTLRY | dstlry.co |

### Manga
| Filename | Publisher | Alias hints for the resolver |
|---|---|---|
| `shueisha.svg` | Shueisha | Shueisha, 集英社, Weekly Shōnen Jump, Shonen Jump |
| `kodansha.svg` | Kodansha | Kodansha, Kodansha USA, 講談社 |
| `shogakukan.svg` | Shogakukan | Shogakukan, 小学館 |
| `kadokawa.svg` | Kadokawa | Kadokawa, KADOKAWA, ASCII Media Works |
| `square-enix.svg` | Square Enix | Square Enix, Square Enix Manga, Gangan |
| `yen-press.svg` | Yen Press | Yen Press |
| `seven-seas.svg` | Seven Seas | Seven Seas Entertainment |

## Notes

- **Wikimedia Commons** usually has the best-licensed publisher SVGs — search "<publisher> logo",
  filter File type → SVG.
- **simpleicons.org** covers the 4 flagged services and nothing else here.
- **brandfetch.com / worldvectorlogo.com / seeklogo.com** as fallbacks — inspect the SVG, reject
  anything that's a traced bitmap or 500 KB of paths.
- Flags are handled separately (bundled from `flag-icons`, MIT) — not on this list.
