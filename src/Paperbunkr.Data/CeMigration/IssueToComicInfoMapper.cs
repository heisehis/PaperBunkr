using cYo.Projects.ComicRack.Engine;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.CeMigration;

/// <summary>
/// The inverse of <see cref="CeLibraryMigrator.MapStoryFields"/> (ComicInfo → Issue): overlays every
/// Paperbunkr-modeled <see cref="ComicInfo"/> field onto <paramref name="target"/> from an
/// <see cref="Issue"/>'s current database state, using <b>effective</b> values (accepted
/// <see cref="MetadataProposal"/>s factored in via <see cref="IssueMetadataExtensions"/>). Used by
/// the file metadata write-back feature (docs/superpowers/specs/2026-09-03-file-metadata-write-back-
/// design.md) - the caller loads the file's <i>current</i> embedded ComicInfo.xml first, so any
/// element Paperbunkr doesn't model (e.g. <c>AlternateCount</c>, <c>PreferredFrontCover</c>, the
/// <c>&lt;Pages&gt;</c> list) survives untouched; this only overwrites what the metadata editors can
/// change.
///
/// Whole-field overwrite from DB truth, not a diff - a field that's null/empty on the issue is
/// written as <see cref="string.Empty"/> (ComicInfo's own "unset" for string fields) or 0 (its
/// unset for numeric fields), matching how <c>MapStoryFields</c> with its default arguments already
/// treats the forward direction.
/// </summary>
public static class IssueToComicInfoMapper
{
    public static void Apply(Issue issue, ComicInfo target)
    {
        // Series comes from the navigation, not a field on Issue - only overwrite when it's loaded
        // (the write-back service always Includes it; a caller that doesn't gets the file's value
        // left alone rather than blanked).
        if (issue.Series?.Name is { Length: > 0 } seriesName)
        {
            target.Series = seriesName;
        }

        target.Title = Str(issue.EffectiveTitle());
        target.Number = Str(issue.EffectiveNumber());
        target.Count = issue.EffectiveCount() ?? 0;
        target.Volume = int.TryParse(issue.EffectiveVolume(), out int volume) ? volume : 0;
        target.AlternateSeries = Str(issue.AlternateSeries);
        target.AlternateNumber = Str(issue.AlternateNumber);
        target.StoryArc = Str(issue.StoryArc);
        target.SeriesGroup = Str(issue.SeriesGroup);
        target.Summary = Str(issue.Summary);
        target.Notes = Str(issue.Notes);
        target.Review = Str(issue.Review);
        target.Year = issue.EffectiveYear() ?? 0;
        target.Month = issue.Month ?? 0;
        target.Day = issue.Day ?? 0;
        target.Writer = Str(issue.Writer);
        target.Penciller = Str(issue.Penciller);
        target.Inker = Str(issue.Inker);
        target.Colorist = Str(issue.Colorist);
        target.Letterer = Str(issue.Letterer);
        target.CoverArtist = Str(issue.CoverArtist);
        target.Editor = Str(issue.Editor);
        target.Translator = Str(issue.Translator);
        target.Publisher = Str(issue.Publisher);
        target.Imprint = Str(issue.Imprint);
        target.Genre = Str(issue.JoinedGenre());
        target.Tags = Str(issue.JoinedTags());
        target.Web = Str(issue.Web);
        target.LanguageISO = Str(issue.LanguageISO);
        target.Format = Str(issue.EffectiveFormat());
        target.AgeRating = Str(issue.AgeRating);
        target.Characters = Str(issue.Characters);
        target.Teams = Str(issue.Teams);
        target.Locations = Str(issue.Locations);
        target.MainCharacterOrTeam = Str(issue.MainCharacterOrTeam);
        target.ScanInformation = Str(issue.ScanInformation);

        // Personal Issue.Rating has no ComicInfo standard field - it's sidecar-only. CommunityRating
        // is the one rating ComicInfo carries.
        target.CommunityRating = issue.CommunityRating ?? 0f;

        target.BlackAndWhite = issue.ColorMode switch
        {
            ColorMode.BlackAndWhite or ColorMode.Grayscale => YesNo.Yes,
            ColorMode.Color => YesNo.No,
            _ => YesNo.Unknown,
        };

        // Inverse of CeLibraryMigrator.MapMangaField, driven by the series' classification. Manhua/
        // Manhwa and any non-manga/comic content type stay Unknown - CE's field only has 3 states
        // and asserting a page direction for them would be a guess.
        bool rightToLeft = issue.Series?.ReadingMode
            is ReadingMode.RightToLeft or ReadingMode.HorizontalContinuousRightToLeft;
        target.Manga = issue.Series?.ContentType switch
        {
            ContentType.Manga => rightToLeft ? MangaYesNo.YesAndRightToLeft : MangaYesNo.Yes,
            ContentType.Comic => MangaYesNo.No,
            _ => MangaYesNo.Unknown,
        };
    }

    private static string Str(string? value) => value ?? string.Empty;
}
