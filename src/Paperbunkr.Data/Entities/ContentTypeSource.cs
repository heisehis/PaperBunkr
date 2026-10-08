namespace Paperbunkr.Data.Entities;

/// <summary>
/// Where a <see cref="Series.ContentType"/> value came from (docs/superpowers/specs/2026-10-06-content-type-auto-classify-design.md).
/// Stored as its string name. Without this the weekly sweep could not tell a deliberate "Unknown" from a never-classified series.
/// </summary>
public enum ContentTypeSource
{
    /// <summary>Not recorded - a series created before provenance existed, or never classified.</summary>
    Unset,

    /// <summary>The file's embedded <c>Manga</c> flag, at series creation.</summary>
    Embedded,

    /// <summary><see cref="Issue.LanguageISO"/>, at series creation or by the sweep.</summary>
    Language,

    /// <summary><c>PublisherContentTypeClassifier</c>, at series creation or by the sweep.</summary>
    Publisher,

    /// <summary>A tracker / metadata provider match (MangaBaka, AniList, MangaDex).</summary>
    Provider,

    /// <summary>A person set it (Library menu, bulk edit, Detail picker, or a confirm-queue action).</summary>
    Manual,

    /// <summary>Present before provenance existed; the migration locked it so no pipeline overrides it.</summary>
    Existing,
}
