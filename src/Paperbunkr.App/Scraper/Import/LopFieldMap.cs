using System;
using System.Collections.Generic;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Scraper.Import;

/// <summary>How the plugin compares a rule's field, which decides the operators that can be carried over.</summary>
public enum LopFieldKind
{
    /// <summary>A text field: <c>is</c>, <c>is not</c>, <c>contains</c>, <c>does not contain</c>, compared case-sensitively.</summary>
    Text,

    /// <summary>A whole-number field: only <c>greater than</c> / <c>less than</c> work in the plugin (it compares an int with the rule's text, so <c>is</c> never matches).</summary>
    Number,

    /// <summary>A Yes/No/Unknown field: only <c>is Yes</c> / <c>is not Yes</c> have a native spelling (our toggle has no "Unknown").</summary>
    Toggle,
}

/// <summary>
/// The plugin's exclude-rule field names (<c>name_to_field</c>, <c>locommon.py:57-107</c>) that have an exact equivalent here. Every other name (<c>Counter</c>,
/// <c>First Letter</c>, <c>Start Year</c>, <c>End Month</c>, <c>Added Date</c>, <c>Released Date</c>, <c>Manga</c>, <c>File Format</c>) is not carried over:
/// a rule that cannot be matched exactly would change which files move, so the whole profile is refused instead (see <see cref="ExcludeRuleConverter"/>).
/// </summary>
public static class LopFieldMap
{
    public sealed record Entry(SmartListField Field, LopFieldKind Kind);

    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Age Rating"] = new(SmartListField.AgeRating, LopFieldKind.Text),
        ["Alternate Number"] = new(SmartListField.AlternateNumber, LopFieldKind.Text),
        ["Alternate Series"] = new(SmartListField.AlternateSeries, LopFieldKind.Text),
        ["Characters"] = new(SmartListField.Characters, LopFieldKind.Text),
        ["Colorist"] = new(SmartListField.Colorist, LopFieldKind.Text),
        ["Cover Artist"] = new(SmartListField.CoverArtist, LopFieldKind.Text),
        ["Editor"] = new(SmartListField.Editor, LopFieldKind.Text),
        ["File Name"] = new(SmartListField.File, LopFieldKind.Text),
        ["File Path"] = new(SmartListField.FullPath, LopFieldKind.Text),
        ["Format"] = new(SmartListField.Format, LopFieldKind.Text),
        ["Genre"] = new(SmartListField.Genre, LopFieldKind.Text),
        ["Imprint"] = new(SmartListField.Imprint, LopFieldKind.Text),
        ["Inker"] = new(SmartListField.Inker, LopFieldKind.Text),
        ["Language"] = new(SmartListField.LanguageISO, LopFieldKind.Text),
        ["Letterer"] = new(SmartListField.Letterer, LopFieldKind.Text),
        ["Locations"] = new(SmartListField.Locations, LopFieldKind.Text),
        ["Main Character Or Team"] = new(SmartListField.MainCharacterOrTeam, LopFieldKind.Text),
        ["Notes"] = new(SmartListField.Notes, LopFieldKind.Text),
        ["Number"] = new(SmartListField.Number, LopFieldKind.Text),
        ["Penciller"] = new(SmartListField.Penciller, LopFieldKind.Text),
        ["Publisher"] = new(SmartListField.Publisher, LopFieldKind.Text),
        ["Review"] = new(SmartListField.Review, LopFieldKind.Text),
        ["Scan Information"] = new(SmartListField.ScanInformation, LopFieldKind.Text),
        ["Series"] = new(SmartListField.SeriesName, LopFieldKind.Text),
        ["Series Group"] = new(SmartListField.SeriesGroup, LopFieldKind.Text),
        ["Story Arc"] = new(SmartListField.StoryArc, LopFieldKind.Text),
        ["Tags"] = new(SmartListField.Tags, LopFieldKind.Text),
        ["Teams"] = new(SmartListField.Teams, LopFieldKind.Text),
        ["Title"] = new(SmartListField.Title, LopFieldKind.Text),
        ["Web"] = new(SmartListField.Web, LopFieldKind.Text),
        ["Writer"] = new(SmartListField.Writer, LopFieldKind.Text),

        ["Alternate Count"] = new(SmartListField.AlternateCount, LopFieldKind.Number),
        ["Count"] = new(SmartListField.Count, LopFieldKind.Number),
        ["Day"] = new(SmartListField.Day, LopFieldKind.Number),
        ["Month"] = new(SmartListField.Month, LopFieldKind.Number),
        ["Rating"] = new(SmartListField.Rating, LopFieldKind.Number),
        ["Read Percentage"] = new(SmartListField.ReadPercentage, LopFieldKind.Number),
        ["Volume"] = new(SmartListField.Volume, LopFieldKind.Number),
        ["Year"] = new(SmartListField.Year, LopFieldKind.Number),

        ["Black And White"] = new(SmartListField.BlackAndWhite, LopFieldKind.Toggle),
        ["Series Complete"] = new(SmartListField.SeriesComplete, LopFieldKind.Toggle),
    };

    public static bool TryGet(string name, out Entry entry) => Entries.TryGetValue(name.Trim(), out entry!);
}
