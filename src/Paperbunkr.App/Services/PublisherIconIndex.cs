using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Paperbunkr.App.Services;

/// <summary>
/// Name/year → file index over CE's publisher icon pack (docs/superpowers/specs/2026-09-25-
/// publisher-icons-and-reader-textures-design.md §A1). Pure: built from bare filenames plus the
/// pack's <c>map.ini</c> text, so it is unit-testable without bundled assets.
///
/// Mirrors CE's key rules (<c>Program.SplitIconKeysWithYearAndMonth</c>): a filename splits on
/// <c>#</c> and <c>,</c> into aliases, matched case-insensitively; <c>Name(1977-2004)</c> is an
/// era (months optional: <c>(2016-2024_11)</c>), <c>Name(1990)</c> a whole-year key and
/// <c>Name(2024_12)</c> a single-month key. An issue tries its exact <c>year_month</c> first, then
/// its <c>year</c>, like CE's <c>ComicBook.GetIconsInternal</c>. Deviation: a spaced range
/// <c>(2019 - 2021)</c> is accepted (CE's regex ignores it).
/// </summary>
public sealed class PublisherIconIndex
{
    private static readonly Regex EraRegex = new(
        @"^(?<base>.*?)\s*\(\s*(?<start>\d{4})(?:_(?<sm>\d{2}))?\s*(?:-\s*(?<end>\d{4})(?:_(?<em>\d{2}))?)?\s*\)$",
        RegexOptions.Compiled);

    private sealed class Entry
    {
        public string? Undated;
        // Start/End are months since year 0 (year * 12 + month - 1), inclusive. MonthOnly marks a
        // "Name(YYYY_MM)" key, which CE matches for that exact month only, never for a bare year.
        public readonly List<(int Start, int End, bool MonthOnly, string File)> Eras = new();
    }

    private readonly Dictionary<string, Entry> _byKey = new(StringComparer.OrdinalIgnoreCase);

    public int FileCount { get; private set; }

    public static PublisherIconIndex Build(IEnumerable<string> fileNames, string? mapIni = null)
    {
        var index = new PublisherIconIndex();

        // Ordinal-sorted so two files claiming the same key/year resolve deterministically.
        var files = fileNames
            .Where(f => IsImage(f))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
        index.FileCount = files.Count;

        foreach (string file in files)
        {
            foreach (string part in SplitKeys(Path.GetFileNameWithoutExtension(file)))
            {
                index.Add(part, file);
            }
        }

        // map.ini: "file.ext=extra key" lines (';' comments) - CE's escape hatch for names that
        // cannot be spelled in a filename ("AiT/Planet Lar").
        if (!string.IsNullOrWhiteSpace(mapIni))
        {
            var known = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
            foreach (string raw in mapIni.Split('\n'))
            {
                string line = raw.Trim();
                int eq = line.IndexOf('=');
                if (line.Length == 0 || line[0] == ';' || eq <= 0)
                {
                    continue;
                }

                string file = line[..eq].Trim();
                if (known.Contains(file))
                {
                    string actual = files.First(f => string.Equals(f, file, StringComparison.OrdinalIgnoreCase));
                    foreach (string part in SplitKeys(line[(eq + 1)..].Trim()))
                    {
                        index.Add(part, actual);
                    }
                }
            }
        }

        return index;
    }

    private static bool IsImage(string file) =>
        file.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
        || file.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
        || file.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> SplitKeys(string value) =>
        value.Split('#', ',').Select(p => p.Trim()).Where(p => p.Length > 0);

    private void Add(string key, string file)
    {
        Match m = EraRegex.Match(key);
        if (m.Success && m.Groups["base"].Value.Length > 0)
        {
            int startYear = int.Parse(m.Groups["start"].Value);
            int? startMonth = m.Groups["sm"].Success ? Math.Clamp(int.Parse(m.Groups["sm"].Value), 1, 12) : null;
            bool isRange = m.Groups["end"].Success;
            int endYear = isRange ? int.Parse(m.Groups["end"].Value) : startYear;
            int? endMonth = m.Groups["em"].Success ? Math.Clamp(int.Parse(m.Groups["em"].Value), 1, 12) : null;

            // A bare "(YYYY_MM)" is one month; a bare "(YYYY)" is that whole year.
            bool monthOnly = !isRange && startMonth is not null;
            int start = startYear * 12 + (startMonth ?? 1) - 1;
            int end = monthOnly ? start : endYear * 12 + (endMonth ?? 12) - 1;
            if (end < start)
            {
                (start, end) = (end, start);
            }

            foreach (string k in KeyForms(m.Groups["base"].Value))
            {
                EntryFor(k).Eras.Add((start, end, monthOnly, file));
            }

            return;
        }

        foreach (string k in KeyForms(key))
        {
            Entry e = EntryFor(k);
            e.Undated ??= file;
        }
    }

    /// <summary>The literal key plus its suffix-stripped form, so "Aftershock" and
    /// "Aftershock Comics" reach the same file whichever spelling the pack used.</summary>
    private static IEnumerable<string> KeyForms(string name)
    {
        string lower = name.Trim().ToLowerInvariant();
        yield return lower;
        string normalised = MarkResolver.NormalisePublisher(lower);
        if (normalised.Length > 0 && normalised != lower)
        {
            yield return normalised;
        }
    }

    private Entry EntryFor(string key)
    {
        if (!_byKey.TryGetValue(key, out Entry? e))
        {
            _byKey[key] = e = new Entry();
        }

        return e;
    }

    /// <summary>Each lookup name plus its suffix-stripped form ("Ablaze Comics" also tries "ablaze").</summary>
    private static IEnumerable<string> Expand(IEnumerable<string> names) =>
        names.SelectMany(n => KeyForms(n)).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>The era file for the first name that has one: an exact <paramref name="month"/> match
    /// when the month is known (the only way a single-month key ever matches), else any era
    /// overlapping <paramref name="year"/>.</summary>
    public string? FindEra(IEnumerable<string> names, int year, int? month = null)
    {
        foreach (string name in Expand(names))
        {
            if (!_byKey.TryGetValue(name, out Entry? e))
            {
                continue;
            }

            if (month is >= 1 and <= 12)
            {
                int ym = year * 12 + month.Value - 1;
                foreach (var era in e.Eras)
                {
                    if (ym >= era.Start && ym <= era.End)
                    {
                        return era.File;
                    }
                }
            }

            int yearStart = year * 12, yearEnd = yearStart + 11;
            foreach (var era in e.Eras)
            {
                if (!era.MonthOnly && era.Start <= yearEnd && era.End >= yearStart)
                {
                    return era.File;
                }
            }
        }

        return null;
    }

    /// <summary>The undated ("just the publisher name") file for the first name that has one.</summary>
    public string? FindUndated(IEnumerable<string> names)
    {
        foreach (string name in Expand(names))
        {
            if (_byKey.TryGetValue(name, out Entry? e) && e.Undated is not null)
            {
                return e.Undated;
            }
        }

        return null;
    }

    /// <summary>The most recent era's file (latest end year) - the fallback when the issue has no
    /// year or none of the eras cover it.</summary>
    public string? FindNewestEra(IEnumerable<string> names)
    {
        foreach (string name in Expand(names))
        {
            if (_byKey.TryGetValue(name, out Entry? e) && e.Eras.Count > 0)
            {
                return e.Eras.OrderByDescending(x => x.End).ThenByDescending(x => x.Start).ThenBy(x => x.File, StringComparer.Ordinal).First().File;
            }
        }

        return null;
    }
}
