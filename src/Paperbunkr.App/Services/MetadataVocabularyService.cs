using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Services;

/// <summary>
/// Which metadata field a vocabulary list is for. One entry per editable field that offers
/// autocomplete / a dropdown in the Issue Properties + Bulk editors
/// (docs/superpowers/specs/2026-09-05-metadata-editor-affordances-design.md §3.1).
/// </summary>
public enum VocabField
{
    Title, AlternateSeries, StoryArc, SeriesGroup,
    Publisher, Imprint, Format, AgeRating, BookAge, LanguageIso,
    Writer, Penciller, Inker, Colorist, Letterer, CoverArtist, Editor, Translator,
    Genre, Tags, Characters, Teams, MainCharacterOrTeam, Locations,
}

/// <summary>
/// The per-field candidate lists produced by <see cref="MetadataVocabularyService.Build"/> -
/// sorted, deduped, never null. Indexing an absent field yields an empty list, so a partially
/// built (or <see cref="Empty"/>) vocabulary never breaks a binding.
/// </summary>
public sealed class MetadataVocabulary
{
    private readonly IReadOnlyDictionary<VocabField, IReadOnlyList<string>> _lists;

    internal MetadataVocabulary(IReadOnlyDictionary<VocabField, IReadOnlyList<string>> lists)
    {
        _lists = lists;
    }

    public IReadOnlyList<string> this[VocabField field] =>
        _lists.TryGetValue(field, out var list) ? list : Array.Empty<string>();

    /// <summary>Every field empty - what the editors bind to until the background build lands.</summary>
    public static MetadataVocabulary Empty { get; } =
        new(new Dictionary<VocabField, IReadOnlyList<string>>());
}

/// <summary>
/// Builds autocomplete/dropdown vocabularies from the whole library, matching CE's
/// <c>DefaultLists.GetComicFieldList</c> behaviour (distinct values across every book), merged
/// with Paperbunkr's shipped static catalogs for Format / Age Rating / Book Age / Language
/// (docs/superpowers/specs/2026-09-05-metadata-editor-affordances-design.md §3.1).
///
/// Pure function of its <see cref="PaperbunkrDbContext"/> argument - no shared state - so it is
/// safe to call from a background thread; both editors do exactly that in their <c>Load</c>.
/// </summary>
public static class MetadataVocabularyService
{
    /// <summary>List-typed fields (comma-separated) - split into individual tokens before dedup so
    /// the candidate list is names, not prior comma-strings. Mirrors <c>BulkFieldRegistry</c>'s own
    /// <c>isList: true</c> set.</summary>
    private static readonly HashSet<VocabField> ListFields = new()
    {
        VocabField.Writer, VocabField.Penciller, VocabField.Inker, VocabField.Colorist,
        VocabField.Letterer, VocabField.CoverArtist, VocabField.Editor, VocabField.Translator,
        VocabField.Genre, VocabField.Tags, VocabField.Characters, VocabField.Teams,
        VocabField.MainCharacterOrTeam, VocabField.Locations,
    };

    public static MetadataVocabulary Build(PaperbunkrDbContext context)
    {
        var rows = context.Issues.AsNoTracking()
            .Select(i => new Row
            {
                Title = i.Title,
                AlternateSeries = i.AlternateSeries,
                StoryArc = i.StoryArc,
                SeriesGroup = i.SeriesGroup,
                Publisher = i.Publisher,
                Imprint = i.Imprint,
                Format = i.Format,
                AgeRating = i.AgeRating,
                BookAge = i.BookAge,
                LanguageIso = i.LanguageISO,
                Writer = i.Writer,
                Penciller = i.Penciller,
                Inker = i.Inker,
                Colorist = i.Colorist,
                Letterer = i.Letterer,
                CoverArtist = i.CoverArtist,
                Editor = i.Editor,
                Translator = i.Translator,
                Characters = i.Characters,
                Teams = i.Teams,
                MainCharacterOrTeam = i.MainCharacterOrTeam,
                Locations = i.Locations,
                Genre = i.Tags.Where(t => t.Field == IssueTagField.Genre).Select(t => t.Value).ToList(),
                Tags = i.Tags.Where(t => t.Field == IssueTagField.Tags).Select(t => t.Value).ToList(),
            })
            .ToList();

        IEnumerable<string?> Column(Func<Row, string?> pick) => rows.Select(pick);

        var lists = new Dictionary<VocabField, List<string>>
        {
            [VocabField.Title] = Distinct(VocabField.Title, Column(r => r.Title)),
            [VocabField.AlternateSeries] = Distinct(VocabField.AlternateSeries, Column(r => r.AlternateSeries)),
            [VocabField.StoryArc] = Distinct(VocabField.StoryArc, Column(r => r.StoryArc)),
            [VocabField.SeriesGroup] = Distinct(VocabField.SeriesGroup, Column(r => r.SeriesGroup)),
            [VocabField.Publisher] = Distinct(VocabField.Publisher, Column(r => r.Publisher)),
            [VocabField.Imprint] = Distinct(VocabField.Imprint, Column(r => r.Imprint)),
            [VocabField.Writer] = Distinct(VocabField.Writer, Column(r => r.Writer)),
            [VocabField.Penciller] = Distinct(VocabField.Penciller, Column(r => r.Penciller)),
            [VocabField.Inker] = Distinct(VocabField.Inker, Column(r => r.Inker)),
            [VocabField.Colorist] = Distinct(VocabField.Colorist, Column(r => r.Colorist)),
            [VocabField.Letterer] = Distinct(VocabField.Letterer, Column(r => r.Letterer)),
            [VocabField.CoverArtist] = Distinct(VocabField.CoverArtist, Column(r => r.CoverArtist)),
            [VocabField.Editor] = Distinct(VocabField.Editor, Column(r => r.Editor)),
            [VocabField.Translator] = Distinct(VocabField.Translator, Column(r => r.Translator)),
            [VocabField.Teams] = Distinct(VocabField.Teams, Column(r => r.Teams)),
            [VocabField.MainCharacterOrTeam] = Distinct(VocabField.MainCharacterOrTeam, Column(r => r.MainCharacterOrTeam)),
            [VocabField.Locations] = Distinct(VocabField.Locations, Column(r => r.Locations)),
            [VocabField.Genre] = Distinct(VocabField.Genre, rows.SelectMany(r => r.Genre)),
            [VocabField.Tags] = Distinct(VocabField.Tags, rows.SelectMany(r => r.Tags)),
            [VocabField.Characters] = Distinct(
                VocabField.Characters,
                Column(r => r.Characters).Concat(context.Characters.AsNoTracking().Select(c => c.Name).ToList())),
            [VocabField.Format] = Distinct(
                VocabField.Format,
                Column(r => r.Format).Concat(FormatSignalCatalog.CeDefaultFormats).Concat(SpecialFormatCatalog.KavitaOnlyAdditions)),
            [VocabField.AgeRating] = Distinct(
                VocabField.AgeRating,
                Column(r => r.AgeRating).Concat(MarkResolver.Instance.AgeRatingCanonicals)),
            [VocabField.BookAge] = Distinct(
                VocabField.BookAge,
                Column(r => r.BookAge).Concat(Enum.GetValues<ComicAge>().Select(a => ComicAgeCatalog.All[a].CeListLabel))),
            [VocabField.LanguageIso] = Distinct(
                VocabField.LanguageIso,
                Column(r => r.LanguageIso).Concat(NeutralCultureLabels())),
        };

        return new MetadataVocabulary(
            lists.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value));
    }

    /// <summary>Culture labels for the Language dropdown: <c>"English — en"</c>. The metadata
    /// editors store the bare ISO code (see <c>LanguageNormalizer</c>), so the label carries both
    /// the readable name and the code the normalizer maps back to.</summary>
    private static IEnumerable<string> NeutralCultureLabels() =>
        CultureInfo.GetCultures(CultureTypes.NeutralCultures)
            .Where(c => !string.IsNullOrEmpty(c.Name) && !string.IsNullOrEmpty(c.TwoLetterISOLanguageName))
            .Select(c => $"{c.DisplayName} — {c.TwoLetterISOLanguageName}");

    private static List<string> Distinct(VocabField field, IEnumerable<string?> values)
    {
        IEnumerable<string> flattened = ListFields.Contains(field)
            ? values.SelectMany(v => ListFieldTokens.Parse(v))
            : values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim());

        return flattened
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed class Row
    {
        public string? Title { get; set; }
        public string? AlternateSeries { get; set; }
        public string? StoryArc { get; set; }
        public string? SeriesGroup { get; set; }
        public string? Publisher { get; set; }
        public string? Imprint { get; set; }
        public string? Format { get; set; }
        public string? AgeRating { get; set; }
        public string? BookAge { get; set; }
        public string? LanguageIso { get; set; }
        public string? Writer { get; set; }
        public string? Penciller { get; set; }
        public string? Inker { get; set; }
        public string? Colorist { get; set; }
        public string? Letterer { get; set; }
        public string? CoverArtist { get; set; }
        public string? Editor { get; set; }
        public string? Translator { get; set; }
        public string? Characters { get; set; }
        public string? Teams { get; set; }
        public string? MainCharacterOrTeam { get; set; }
        public string? Locations { get; set; }
        public List<string> Genre { get; set; } = new();
        public List<string> Tags { get; set; } = new();
    }
}
