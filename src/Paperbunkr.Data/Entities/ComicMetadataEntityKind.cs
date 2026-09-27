namespace Paperbunkr.Data.Entities;

/// <summary>The kinds of thing a <see cref="ComicMetadataExternalId"/> row can point at - the original
/// five first-class metadata entities, plus <see cref="Issue"/>/<see cref="Series"/> themselves (docs/
/// superpowers/specs/2026-09-24-comicvine-scraper-fidelity-design.md §2.7 - CE persists a durable
/// comicvine_issue/comicvine_volume identity on every scraped book, pluginbookdata.py:23-24, verified;
/// this reuses the same generic table rather than adding two more one-off columns).</summary>
public enum ComicMetadataEntityKind
{
    Character = 0,
    Team = 1,
    Location = 2,
    Creator = 3,
    Publisher = 4,
    Issue = 5,
    Series = 6,
}
