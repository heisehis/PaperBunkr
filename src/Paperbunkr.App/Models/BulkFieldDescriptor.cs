using System;
using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>Which input control a <see cref="BulkFieldDescriptor"/> row renders as.</summary>
public enum FieldKind
{
    Text,
    Boolean,
    Rating,
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
    Action<Issue, string?> Set);

/// <summary>
/// The full bulk-editable field set, verified field-by-field against CE's real
/// <c>MultipleComicBooksDialog.Designer.cs</c>/<c>.cs</c> (docs/superpowers/specs/
/// 2026-08-07-bulk-issue-editing-design.md §3) - <c>StoryArcNumber</c>/<c>Series</c>/
/// <c>SeriesComplete</c>/<c>Manga</c>/<c>EnableProposed</c>/<c>AlternateCount</c> are deliberately
/// absent, see the spec for why each one is excluded.
/// </summary>
public static class BulkFieldRegistry
{
    public const string Main = "Main";
    public const string Artists = "Artists";
    public const string PlotAndNotes = "Plot & Notes";

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

    private static BulkFieldDescriptor Text(string label, string group, Func<Issue, string?> get, Action<Issue, string?> set, bool isList = false) =>
        new(label, group, FieldKind.Text, isList, get, (i, v) => set(i, Norm(v)));

    private static BulkFieldDescriptor Numeric(string label, string group, Func<Issue, int?> get, Action<Issue, int?> set) =>
        new(label, group, FieldKind.Text, IsListField: false, i => get(i)?.ToString(), (i, v) => set(i, ParseInt(v)));

    public static readonly BulkFieldDescriptor[] All =
    {
        // ===== Main =====
        Text("Number", Main, i => i.Number, (i, v) => i.Number = v),
        Numeric("Count", Main, i => i.Count, (i, v) => i.Count = v),
        Numeric("Volume", Main, i => i.Volume, (i, v) => i.Volume = v),
        Numeric("Year", Main, i => i.Year, (i, v) => i.Year = v),
        Numeric("Month", Main, i => i.Month, (i, v) => i.Month = v),
        Numeric("Day", Main, i => i.Day, (i, v) => i.Day = v),
        Text("Title", Main, i => i.Title, (i, v) => i.Title = v),
        Text("Alternate Series", Main, i => i.AlternateSeries, (i, v) => i.AlternateSeries = v),
        Text("Alternate Number", Main, i => i.AlternateNumber, (i, v) => i.AlternateNumber = v),
        Text("Series Group", Main, i => i.SeriesGroup, (i, v) => i.SeriesGroup = v),
        Text("Story Arc", Main, i => i.StoryArc, (i, v) => i.StoryArc = v),
        Text("Genre", Main, i => i.Genre, (i, v) => i.Genre = v, isList: true),
        Text("Tags", Main, i => i.Tags, (i, v) => i.Tags = v, isList: true),
        Text("Publisher", Main, i => i.Publisher, (i, v) => i.Publisher = v),
        Text("Imprint", Main, i => i.Imprint, (i, v) => i.Imprint = v),
        Text("Format", Main, i => i.Format, (i, v) => i.Format = v),
        Text("Age Rating", Main, i => i.AgeRating, (i, v) => i.AgeRating = v),
        Text("Language (ISO)", Main, i => i.LanguageISO, (i, v) => i.LanguageISO = v),
        new("Black and White", Main, FieldKind.Boolean, IsListField: false,
            i => i.BlackAndWhite ? "true" : "false", (i, v) => i.BlackAndWhite = v == "true"),
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
