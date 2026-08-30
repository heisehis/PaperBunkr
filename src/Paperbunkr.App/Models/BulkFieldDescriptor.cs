using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Models;

/// <summary>Which input control a <see cref="BulkFieldDescriptor"/> row renders as.</summary>
public enum FieldKind
{
    Text,
    Boolean,
    Rating,

    /// <summary>Fixed-choice picker (docs/superpowers/specs/2026-08-16-manga-content-type-classification-design.md §1) - candidate values live in <see cref="BulkFieldDescriptor.Options"/>, rendered as a flyout of buttons (this app's established enum-picker idiom, e.g. ReaderScreen.axaml's reading-mode/fit-mode pickers) rather than a native ComboBox.</summary>
    Enum,
}

/// <summary>
/// One bulk-editable field, decoupled from any specific <see cref="Issue"/> instance
/// (docs/superpowers/specs/2026-08-07-bulk-issue-editing-design.md §3). <see cref="Get"/>/
/// <see cref="Set"/> normalize every field to <c>string</c>, reusing the single-book Issue
/// Properties editor's exact conventions (numeric fields round-trip through
/// <see cref="int.TryParse(string?, out int)"/>, scalar text nulls out on empty/whitespace).
/// </summary>
public sealed record BulkFieldDescriptor(
    string Label,
    string Group,
    FieldKind Kind,
    bool IsListField,
    Func<Issue, string?> Get,
    Action<Issue, string?> Set,
    IReadOnlyList<string>? Options = null,
    IReadOnlyList<string>? Autocomplete = null);

/// <summary>
/// The full bulk-editable field set, verified field-by-field against CE's real
/// <c>MultipleComicBooksDialog.Designer.cs</c>/<c>.cs</c> (docs/superpowers/specs/
/// 2026-08-07-bulk-issue-editing-design.md §3) - <c>StoryArcNumber</c>/<c>Series</c>/<c>Manga</c>/
/// <c>EnableProposed</c>/<c>AlternateCount</c> are deliberately absent, see the spec for why each
/// one is excluded. CE's own <c>SeriesComplete</c> now has a real Paperbunkr home below (the
/// Series-level <c>Status</c> row, docs/superpowers/specs/2026-08-18-metadata-model-ui-gaps-status-
/// and-bookmarks-design.md) - not the same shape as CE's per-issue Yes/No/Unknown flag, which is
/// <see cref="Issue.IsFinalIssue"/> instead, edited on the single-issue Issue Properties screen.
/// </summary>
public static class BulkFieldRegistry
{
    public const string Main = "Main";
    public const string Artists = "Artists";
    public const string PlotAndNotes = "Plot & Notes";

    /// <summary>Shared with BulkIssuePropertiesScreenViewModel, which needs to find this specific row to wire the bespoke Reading Direction row's conditional visibility (docs/superpowers/specs/2026-08-16-manga-content-type-classification-design.md §2).</summary>
    public const string ContentTypeLabel = "Content Type";

    private static string? Norm(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static int? ParseInt(string? value) => int.TryParse(value, out int result) ? result : null;

    /// <summary>
    /// Shared field-access lookup for the Detail screen's issue-focused display (docs/superpowers/specs/
    /// 2026-08-07-detail-screen-issue-focus-design.md §3) - the registry doubling as the "gathering
    /// tool" for both bulk editing and read-only display, one field-access path instead of two.
    /// Throws on an unknown label (a programming error - every caller passes a compile-time-known
    /// literal) rather than returning null and deferring the failure to a confusing NullReferenceException.
    /// </summary>
    public static BulkFieldDescriptor Find(string label) =>
        All.FirstOrDefault(f => f.Label == label)
        ?? throw new ArgumentException($"No BulkFieldDescriptor with label '{label}'.", nameof(label));

    private static BulkFieldDescriptor Text(string label, string group, Func<Issue, string?> get, Action<Issue, string?> set, bool isList = false, IReadOnlyList<string>? autocomplete = null) =>
        new(label, group, FieldKind.Text, isList, get, (i, v) => set(i, Norm(v)), Autocomplete: autocomplete);

    private static BulkFieldDescriptor Numeric(string label, string group, Func<Issue, int?> get, Action<Issue, int?> set) =>
        new(label, group, FieldKind.Text, IsListField: false, i => get(i)?.ToString(), (i, v) => set(i, ParseInt(v)));

    public static readonly BulkFieldDescriptor[] All =
    {
        // ===== Main =====
        // Number/Volume/Year read through Effective* (docs/superpowers/specs/2026-08-17-metadata-
        // model-phase2a-metadata-proposals-design.md) so a filename-inferred value still displays
        // here even though the raw Issue field is null - Set is unchanged, still writes directly to
        // the raw field (a real edit naturally supersedes any old Accepted proposal, since the
        // resolver always prefers a stored value first).
        Text("Number", Main, i => i.EffectiveNumber(), (i, v) => i.Number = v),
        Numeric("Count", Main, i => i.Count, (i, v) => i.Count = v),
        Text("Volume", Main, i => i.EffectiveVolume(), (i, v) => i.Volume = v),
        Numeric("Year", Main, i => i.EffectiveYear(), (i, v) => i.Year = v),
        Numeric("Month", Main, i => i.Month, (i, v) => i.Month = v),
        Numeric("Day", Main, i => i.Day, (i, v) => i.Day = v),
        Text("Title", Main, i => i.Title, (i, v) => i.Title = v),
        Text("Alternate Series", Main, i => i.AlternateSeries, (i, v) => i.AlternateSeries = v),
        Text("Alternate Number", Main, i => i.AlternateNumber, (i, v) => i.AlternateNumber = v),
        Text("Series Group", Main, i => i.SeriesGroup, (i, v) => i.SeriesGroup = v),
        Text("Story Arc", Main, i => i.StoryArc, (i, v) => i.StoryArc = v),
        // Genre/Tags read/write through the structured IssueTag collection now (docs/superpowers/
        // specs/2026-08-23-weighted-categorized-tags-design.md) - Get still returns the same
        // joined comma string as before, and Set still diffs rather than replaces (MergeFrom),
        // preserving Category/Weight for any value that survives the edit.
        Text("Genre", Main, i => i.JoinedGenre(), (i, v) => i.MergeFrom(IssueTagField.Genre, new[] { v }), isList: true),
        Text("Tags", Main, i => i.JoinedTags(), (i, v) => i.MergeFrom(IssueTagField.Tags, new[] { v }), isList: true),
        // Series-owned, reached through Issue.Series - the first bulk field to write through to the
        // owning Series rather than the Issue itself (docs/superpowers/specs/2026-08-16-manga-
        // content-type-classification-design.md §1). Bulk-saving iterates selected Issues once per
        // staged field, so a selection spanning multiple series naturally converges to "every
        // touched series ends up at the new value" with no special batching needed.
        new(ContentTypeLabel, Main, FieldKind.Enum, IsListField: false,
            i => i.Series.ContentType.ToString(),
            (i, v) => i.Series.ContentType = Enum.Parse<ContentType>(v ?? nameof(ContentType.Unknown)),
            Options: Enum.GetNames<ContentType>()),
        // Series-owned, same shape as ContentType above (docs/superpowers/specs/2026-08-18-
        // metadata-model-ui-gaps-status-and-bookmarks-design.md).
        new("Status", Main, FieldKind.Enum, IsListField: false,
            i => i.Series.Status.ToString(),
            (i, v) => i.Series.Status = Enum.Parse<SeriesStatus>(v ?? nameof(SeriesStatus.Unknown)),
            Options: Enum.GetNames<SeriesStatus>()),
        // Series-owned, same shape as Status above (docs/superpowers/specs/2026-08-19-metadata-
        // model-reading-status-design.md) - the user's own reading-progress relationship with the
        // series, not the publisher's release status.
        new("Reading Status", Main, FieldKind.Enum, IsListField: false,
            i => i.Series.ReadingStatus.ToString(),
            (i, v) => i.Series.ReadingStatus = Enum.Parse<ReadingStatus>(v ?? nameof(ReadingStatus.Unknown)),
            Options: Enum.GetNames<ReadingStatus>()),
        Text("Publisher", Main, i => i.Publisher, (i, v) => i.Publisher = v),
        Text("Imprint", Main, i => i.Imprint, (i, v) => i.Imprint = v),
        // Autocomplete seeded with CE's 16 shipped [Book Formats] defaults (docs/superpowers/specs/
        // 2026-08-27-metadata-model-phase4e-format-signal-suggestions-design.md) plus
        // SpecialFormatCatalog's Kavita-only additions (docs/superpowers/specs/2026-08-28-series-
        // detail-specials-tab-design.md) - still free text.
        Text("Format", Main, i => i.Format, (i, v) => i.Format = v,
            autocomplete: FormatSignalCatalog.CeDefaultFormats.Union(SpecialFormatCatalog.KavitaOnlyAdditions, StringComparer.OrdinalIgnoreCase).ToList()),
        // Book Age - free text (docs/superpowers/specs/2026-08-27-metadata-model-phase4g-age-
        // progression-design.md), autocomplete seeded with CE's five [Book Ages] defaults.
        Text("Book Age", Main, i => i.BookAge, (i, v) => i.BookAge = v,
            autocomplete: Enum.GetValues<ComicAge>().Select(a => ComicAgeCatalog.All[a].CeListLabel).ToArray()),
        Text("Age Rating", Main, i => i.AgeRating, (i, v) => i.AgeRating = v),
        Text("Language (ISO)", Main, i => i.LanguageISO, (i, v) => i.LanguageISO = v),
        new("Color Mode", Main, FieldKind.Enum, IsListField: false,
            i => i.ColorMode.ToString(),
            (i, v) => i.ColorMode = Enum.Parse<ColorMode>(v ?? nameof(ColorMode.Unknown)),
            Options: Enum.GetNames<ColorMode>()),
        new("My Rating", Main, FieldKind.Rating, IsListField: false,
            i => i.Rating.HasValue ? ((int)i.Rating.Value).ToString() : null,
            (i, v) => i.Rating = ParseInt(v)),
        new("Community Rating", Main, FieldKind.Rating, IsListField: false,
            i => i.CommunityRating.HasValue ? ((int)i.CommunityRating.Value).ToString() : null,
            (i, v) => i.CommunityRating = ParseInt(v)),

        // ===== Artists (all list fields, per CE's own listFields set) =====
        Text("Writer", Artists, i => i.Writer, (i, v) => i.Writer = v, isList: true),
        Text("Penciller", Artists, i => i.Penciller, (i, v) => i.Penciller = v, isList: true),
        Text("Inker", Artists, i => i.Inker, (i, v) => i.Inker = v, isList: true),
        Text("Colorist", Artists, i => i.Colorist, (i, v) => i.Colorist = v, isList: true),
        Text("Editor", Artists, i => i.Editor, (i, v) => i.Editor = v, isList: true),
        Text("Cover Artist", Artists, i => i.CoverArtist, (i, v) => i.CoverArtist = v, isList: true),
        Text("Translator", Artists, i => i.Translator, (i, v) => i.Translator = v, isList: true),
        Text("Letterer", Artists, i => i.Letterer, (i, v) => i.Letterer = v, isList: true),

        // ===== Plot & Notes =====
        Text("Main Character or Team", PlotAndNotes, i => i.MainCharacterOrTeam, (i, v) => i.MainCharacterOrTeam = v, isList: true),
        Text("Characters", PlotAndNotes, i => i.Characters, (i, v) => i.Characters = v, isList: true),
        Text("Teams", PlotAndNotes, i => i.Teams, (i, v) => i.Teams = v, isList: true),
        Text("Locations", PlotAndNotes, i => i.Locations, (i, v) => i.Locations = v, isList: true),
        Text("Web", PlotAndNotes, i => i.Web, (i, v) => i.Web = v),
        Text("Scan Information", PlotAndNotes, i => i.ScanInformation, (i, v) => i.ScanInformation = v),
        Text("Summary", PlotAndNotes, i => i.Summary, (i, v) => i.Summary = v),
        Text("Notes", PlotAndNotes, i => i.Notes, (i, v) => i.Notes = v),
        Text("Review", PlotAndNotes, i => i.Review, (i, v) => i.Review = v),
    };
}
