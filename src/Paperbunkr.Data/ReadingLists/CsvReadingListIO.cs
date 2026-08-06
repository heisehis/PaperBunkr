using System.Text;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.ReadingLists;

/// <summary>
/// CSV import (docs/superpowers/specs/2026-08-06-reading-lists-design.md §5). Paperbunkr-defined —
/// no CE or CBLManager precedent (CBLManager only ever did CBL) — deliberately symmetric with CBL's
/// match-key fields so it shares <see cref="ReadingListMatcher"/> with <see cref="CblReadingListIO"/>.
/// Header row: <c>Series,Number,Volume,Year,Format</c> — only Series/Number required, rest optional.
/// </summary>
public static class CsvReadingListIO
{
    public static CsvImportResult Import(PaperbunkrDbContext context, string filePath, string? listName = null)
    {
        var lines = File.ReadAllLines(filePath);
        if (lines.Length == 0)
        {
            throw new InvalidDataException("CSV file is empty.");
        }

        var header = ParseLine(lines[0]).Select(h => h.Trim()).ToList();
        int seriesIndex = header.FindIndex(h => string.Equals(h, "Series", StringComparison.OrdinalIgnoreCase));
        int numberIndex = header.FindIndex(h => string.Equals(h, "Number", StringComparison.OrdinalIgnoreCase));
        if (seriesIndex < 0 || numberIndex < 0)
        {
            throw new InvalidDataException("CSV header must include 'Series' and 'Number' columns.");
        }

        int volumeIndex = header.FindIndex(h => string.Equals(h, "Volume", StringComparison.OrdinalIgnoreCase));
        int yearIndex = header.FindIndex(h => string.Equals(h, "Year", StringComparison.OrdinalIgnoreCase));
        int formatIndex = header.FindIndex(h => string.Equals(h, "Format", StringComparison.OrdinalIgnoreCase));

        var list = new ReadingList { Name = listName ?? Path.GetFileNameWithoutExtension(filePath) };
        var skippedRows = new List<string>();
        int sortOrder = 0;
        int placeholderCount = 0;

        for (int lineIndex = 1; lineIndex < lines.Length; lineIndex++)
        {
            string raw = lines[lineIndex];
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var fields = ParseLine(raw);
            string? series = Field(fields, seriesIndex)?.Trim();
            string? number = Field(fields, numberIndex)?.Trim();
            if (string.IsNullOrEmpty(series) || string.IsNullOrEmpty(number))
            {
                skippedRows.Add($"Line {lineIndex + 1}: missing Series or Number.");
                continue;
            }

            int? volume = int.TryParse(Field(fields, volumeIndex), out var v) ? v : null;
            int? year = int.TryParse(Field(fields, yearIndex), out var y) ? y : null;
            string? format = string.IsNullOrWhiteSpace(Field(fields, formatIndex)) ? null : Field(fields, formatIndex)!.Trim();

            var issue = ReadingListMatcher.ResolveOrCreatePlaceholder(context, series, number, volume, year, format);
            if (issue.IsPlaceholder)
            {
                placeholderCount++;
            }

            list.Items.Add(new ReadingListItem { IssueId = issue.Id, SortOrder = sortOrder++ });
        }

        context.ReadingLists.Add(list);
        context.SaveChanges();
        return new CsvImportResult(list, list.Items.Count - placeholderCount, placeholderCount, skippedRows);
    }

    private static string? Field(List<string> fields, int index) => index >= 0 && index < fields.Count ? fields[index] : null;

    /// <summary>Minimal RFC4180-style line splitter: handles quoted fields, embedded commas, and doubled-quote escaping. No external CSV dependency for a five-column format.</summary>
    private static List<string> ParseLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }
}

/// <summary>OwnedCount/PlaceholderCount reflect the resolved issues' current state (whether the placeholder was created just now or already existed from an earlier import), not strictly "created this run" — matching what CBLManager's own import summary reported.</summary>
public record CsvImportResult(ReadingList List, int OwnedCount, int PlaceholderCount, List<string> SkippedRows);
