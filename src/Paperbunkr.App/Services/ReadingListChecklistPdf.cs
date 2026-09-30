using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Paperbunkr.Data.ReadingLists;
using SkiaSharp;

namespace Paperbunkr.App.Services;

public enum ChecklistPaperSize
{
    Letter,
    A4,
}

/// <summary>
/// Draws a reading list's printable checklist as a PDF (docs/superpowers/specs/2026-09-28-reading-lists-organize-and-track-design.md §6,
/// decisions Q13/V4): SkiaSharp's <see cref="SKDocument"/> - already shipped with Avalonia, so no new dependency. Text only: a read
/// checkbox, #, issue (with the note on a second line), year and owned/MISSING, group labels as shaded rows. Rows never split across
/// pages, a group row is never left alone at the bottom, and the column header repeats on every page. Print is always black on
/// white, so the colours here are fixed on purpose (not skin resources). ComicRack CE has no print feature.
/// </summary>
public static class ReadingListChecklistPdf
{
    private const float Margin = 36f;
    private const float RowHeight = 16f;
    private const float NoteRowHeight = 28f;
    private const float GroupRowHeight = 18f;
    private const float HeaderRowHeight = 16f;
    private const float FooterHeight = 20f;

    private static readonly SKColor Ink = new(0x11, 0x11, 0x11);
    private static readonly SKColor Muted = new(0x66, 0x66, 0x66);
    private static readonly SKColor Rule = new(0xDD, 0xDD, 0xDD);
    private static readonly SKColor GroupFill = new(0xEE, 0xEE, 0xEE);
    private static readonly SKColor MissingRed = new(0xB3, 0x26, 0x1E);

    /// <summary>A4 wherever the region is metric, Letter otherwise.</summary>
    public static ChecklistPaperSize DefaultPaperSize() => RegionInfo.CurrentRegion.IsMetric ? ChecklistPaperSize.A4 : ChecklistPaperSize.Letter;

    public static (float Width, float Height) PageSize(ChecklistPaperSize size) => size == ChecklistPaperSize.A4 ? (595f, 842f) : (612f, 792f);

    /// <summary>One printed line: a group header or an issue row.</summary>
    internal sealed record Line(string? Group, ChecklistRow? Row)
    {
        public float Height => Group is not null ? GroupRowHeight : Row!.Note is null ? RowHeight : NoteRowHeight;
    }

    /// <summary>Splits the checklist into pages (exposed for tests). The first page also carries the title block.</summary>
    internal static IReadOnlyList<IReadOnlyList<Line>> Paginate(ChecklistModel model, ChecklistPaperSize size)
    {
        var lines = new List<Line>();
        foreach (var (label, rows) in ReadingListGrouping.Runs(model.Rows, r => r.GroupLabel))
        {
            if (label.Length > 0)
            {
                lines.Add(new Line(label, null));
            }

            lines.AddRange(rows.Select(r => new Line(null, r)));
        }

        var (_, height) = PageSize(size);
        float firstPageTop = Margin + 44f;                  // title + meta line
        var pages = new List<IReadOnlyList<Line>>();
        var page = new List<Line>();
        float y = firstPageTop + HeaderRowHeight;
        float bottom = height - Margin - FooterHeight;
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            // A group row needs at least its first issue row under it on the same page.
            float needed = line.Height + (line.Group is not null && i + 1 < lines.Count ? lines[i + 1].Height : 0);
            if (y + needed > bottom && page.Count > 0)
            {
                pages.Add(page);
                page = new List<Line>();
                y = Margin + HeaderRowHeight;
            }

            page.Add(line);
            y += line.Height;
        }

        pages.Add(page);
        return pages;
    }

    public static void Write(Stream output, ChecklistModel model, ChecklistPaperSize size)
    {
        var pages = Paginate(model, size);
        var (width, height) = PageSize(size);

        using var document = SKDocument.CreatePdf(output, new SKDocumentPdfMetadata { Title = model.Title, Creator = "Paperbunkr", Producer = "Paperbunkr" });
        using var regular = SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default;
        using var bold = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? regular;
        using var italic = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Italic) ?? regular;
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.75f };

        float colCheck = Margin, colNumber = Margin + 18f, colIssue = Margin + 44f;
        float colStatus = width - Margin - 52f, colYear = colStatus - 40f;
        float issueWidth = colYear - colIssue - 8f;

        for (int p = 0; p < pages.Count; p++)
        {
            var canvas = document.BeginPage(width, height);
            float y = Margin;
            if (p == 0)
            {
                DrawText(canvas, model.Title, Margin, y + 14f, 16f, bold, Ink, fill, width - (2 * Margin));
                DrawText(canvas, model.MetaLine, Margin, y + 32f, 9.5f, regular, Muted, fill, width - (2 * Margin));
                y += 44f;
            }

            // Column header (repeats on every page).
            DrawText(canvas, "#", colNumber, y + 11f, 8.5f, bold, Muted, fill);
            DrawText(canvas, "Issue", colIssue, y + 11f, 8.5f, bold, Muted, fill);
            DrawText(canvas, "Year", colYear, y + 11f, 8.5f, bold, Muted, fill);
            DrawText(canvas, "Status", colStatus, y + 11f, 8.5f, bold, Muted, fill);
            y += HeaderRowHeight;
            HorizontalRule(canvas, y, width, stroke, Ink);

            foreach (var line in pages[p])
            {
                if (line.Group is { } group)
                {
                    fill.Color = GroupFill;
                    canvas.DrawRect(Margin, y, width - (2 * Margin), GroupRowHeight, fill);
                    DrawText(canvas, group, Margin + 4f, y + 12.5f, 9.5f, bold, Ink, fill, width - (2 * Margin) - 8f);
                }
                else
                {
                    var row = line.Row!;
                    stroke.Color = Ink;
                    var box = new SKRect(colCheck, y + 4f, colCheck + 9f, y + 13f);
                    if (row.IsRead)
                    {
                        fill.Color = Ink;
                        canvas.DrawRect(box, fill);
                    }
                    else
                    {
                        canvas.DrawRect(box, stroke);
                    }

                    DrawText(canvas, row.Position.ToString(CultureInfo.CurrentCulture), colNumber, y + 12f, 9.5f, regular, Ink, fill);
                    DrawText(canvas, row.Display, colIssue, y + 12f, 9.5f, regular, Ink, fill, issueWidth);
                    if (row.Year is int year)
                    {
                        DrawText(canvas, year.ToString(CultureInfo.InvariantCulture), colYear, y + 12f, 9.5f, regular, Ink, fill);
                    }

                    DrawText(canvas, row.IsOwned ? "owned" : "MISSING", colStatus, y + 12f, 9.5f, row.IsOwned ? regular : bold, row.IsOwned ? Ink : MissingRed, fill);
                    if (row.Note is { } note)
                    {
                        DrawText(canvas, note, colIssue, y + 24f, 8.5f, italic, Muted, fill, issueWidth);
                    }
                }

                y += line.Height;
                HorizontalRule(canvas, y, width, stroke, Rule);
            }

            DrawText(canvas, "Paperbunkr", Margin, height - Margin, 8f, regular, Muted, fill);
            string pageLabel = $"{p + 1} / {pages.Count}";
            using (var font = new SKFont(regular, 8f))
            {
                float w = font.MeasureText(pageLabel);
                DrawText(canvas, pageLabel, width - Margin - w, height - Margin, 8f, regular, Muted, fill);
            }

            document.EndPage();
        }

        document.Close();
    }

    private static void HorizontalRule(SKCanvas canvas, float y, float width, SKPaint stroke, SKColor color)
    {
        stroke.Color = color;
        canvas.DrawLine(Margin, y, width - Margin, y, stroke);
    }

    /// <summary>Draws one line of text, trimmed with an ellipsis to <paramref name="maxWidth"/>. Falls back to a typeface that has the
    /// glyphs when the chosen one doesn't (a Japanese or Cyrillic series name).</summary>
    private static void DrawText(SKCanvas canvas, string text, float x, float y, float size, SKTypeface typeface, SKColor color, SKPaint paint, float maxWidth = float.MaxValue)
    {
        using var fallback = FallbackFor(text, typeface);
        using var font = new SKFont(fallback ?? typeface, size);
        if (font.MeasureText(text) > maxWidth)
        {
            const string ellipsis = "…";
            int keep = text.Length;
            while (keep > 0 && font.MeasureText(text[..keep] + ellipsis) > maxWidth)
            {
                keep--;
            }

            text = text[..keep].TrimEnd() + ellipsis;
        }

        paint.Color = color;
        canvas.DrawText(text, x, y, SKTextAlign.Left, font, paint);
    }

    private static SKTypeface? FallbackFor(string text, SKTypeface typeface)
    {
        using var probe = new SKFont(typeface);
        if (probe.ContainsGlyphs(text))
        {
            return null;
        }

        foreach (var rune in text.EnumerateRunes())
        {
            if (!probe.ContainsGlyph(rune.Value))
            {
                return SKFontManager.Default.MatchCharacter(typeface.FamilyName, typeface.FontStyle, null, rune.Value);
            }
        }

        return null;
    }
}
