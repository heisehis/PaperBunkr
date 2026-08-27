using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>
/// Series-typed counterpart to <see cref="BulkFieldDescriptor"/> (docs/superpowers/specs/
/// 2026-08-24-library-multiselect-slice3-design.md) - a deliberately separate, smaller type rather
/// than generalizing <see cref="BulkFieldDescriptor"/> itself: that type is concretely
/// <c>Func&lt;Issue,...&gt;</c> and its <see cref="BulkFieldViewModel"/> carries Issue-only concepts
/// (rating stars, <c>{Token}</c> template-insert) Series has no equivalent of. Reuses the existing
/// <see cref="FieldKind"/> enum rather than duplicating it.
/// </summary>
public sealed record SeriesBulkFieldDescriptor(
    string Label,
    FieldKind Kind,
    Func<Series, string?> Get,
    Action<Series, string?> Set,
    IReadOnlyList<string>? Options = null,
    string? Caveat = null);

/// <summary>
/// The full series-level bulk-editable field set. No CE precedent exists to verify this against -
/// confirmed by source grep (docs/superpowers/specs/2026-08-24-library-multiselect-slice3-design.md
/// "Context") that CE has no series-level properties editor, single or bulk, at all. Content
/// Type/Status/Reading Status/Reading Mode mirror the four existing single-series per-value context-
/// menu commands (<c>LibraryScreenViewModel.SetSeriesContentType</c> etc.) exactly, same option sets.
/// </summary>
public static class SeriesBulkFieldRegistry
{
    private static string? Norm(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static SeriesBulkFieldDescriptor Text(string label, Func<Series, string?> get, Action<Series, string?> set, string? caveat = null) =>
        new(label, FieldKind.Text, get, (s, v) => set(s, Norm(v)), Caveat: caveat);

    public const string PublisherCaveat = "Not what Library/Detail display - see Issue-level Publisher for that.";

    public const string GenreCaveat = "Not what Library/Detail display - see Issue-level Genre for that.";

    public static readonly SeriesBulkFieldDescriptor[] All =
    {
        Text("Name", s => s.Name, (s, v) => s.Name = v ?? s.Name),
        Text("Sort Name", s => s.SortName, (s, v) => s.SortName = v),
        new("Content Type", FieldKind.Enum,
            s => s.ContentType.ToString(),
            (s, v) => s.ContentType = Enum.Parse<ContentType>(v ?? nameof(ContentType.Unknown)),
            Options: Enum.GetNames<ContentType>()),
        new("Status", FieldKind.Enum,
            s => s.Status.ToString(),
            (s, v) => s.Status = Enum.Parse<SeriesStatus>(v ?? nameof(SeriesStatus.Unknown)),
            Options: Enum.GetNames<SeriesStatus>()),
        new("Reading Status", FieldKind.Enum,
            s => s.ReadingStatus.ToString(),
            (s, v) => s.ReadingStatus = Enum.Parse<ReadingStatus>(v ?? nameof(ReadingStatus.Unknown)),
            Options: Enum.GetNames<ReadingStatus>()),
        new("Reading Mode", FieldKind.Enum,
            s => s.ReadingMode.ToString(),
            (s, v) => s.ReadingMode = Enum.Parse<ReadingMode>(v ?? nameof(ReadingMode.LeftToRight)),
            Options: Enum.GetNames<ReadingMode>()),
        // Publisher/Genre: user's explicit choice to include these despite Series.cs's own doc
        // comment marking them stale/non-authoritative (Issue-level is the real display source) -
        // the Caveat surfaces that in the editor rather than silently hiding it.
        Text("Publisher", s => s.Publisher, (s, v) => s.Publisher = v, caveat: PublisherCaveat),
        Text("Genre", s => s.Genre, (s, v) => s.Genre = v, caveat: GenreCaveat),
        Text("Summary", s => s.Summary, (s, v) => s.Summary = v),
    };
}
