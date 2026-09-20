using System.Globalization;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Naming;

namespace Paperbunkr.Daemon.Import;

/// <summary>
/// Turns the acquisition importer's data (a watched series, a wanted issue, the downloaded file) into a library-relative path using the shared
/// Organizer-grammar engine (<see cref="TemplateEvaluator"/>), replacing the importer's own <c>NameTemplate</c>
/// (docs/superpowers/specs/2026-09-20-cluster-library-manager-into-core-design.md section 5).
/// <para>
/// The engine evaluates against an <see cref="Issue"/>, so a transient one is built here from what the importer knows; it is never saved.
/// Values an issue can't carry (<c>volumeyear</c>, the file's own name) travel in <see cref="TemplateContext.Extra"/>.
/// After evaluation the result is split on <c>/</c> and <c>\</c>, every segment goes through the CE <see cref="Sanitizer"/>, and then the importer's
/// own Windows safety layer (device names, length), and empty segments are dropped so no folder is ever named "".
/// </para>
/// </summary>
public static class ImportNaming
{
    /// <summary>The Organizer-grammar equivalent of the original default <c>{publisher}/{series} ({volumeyear})/{series} #{number:000}</c>.</summary>
    public const string DefaultTemplate = AcquisitionSettings.DefaultRenameTemplate;

    public static string FormatPath(string template, WatchedSeries watched, WantedIssue wanted, string sourcePath, string extension)
    {
        var (issue, context) = Build(watched, wanted, sourcePath);
        return ToPath(TemplateEvaluator.Evaluate(template, issue, context), extension);
    }

    /// <summary>The template's problem, or <c>null</c> when it is usable. Evaluates it against a sample issue so unknown tokens are found, not just bad brackets.</summary>
    public static string? Validate(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return "The naming template can't be empty.";
        }

        try
        {
            var (issue, context) = Build(SampleSeries, SampleIssue, "Spawn 263 (2016).cbr");
            var text = TemplateEvaluator.Evaluate(template, issue, context);
            if (text.Contains('{') || text.Contains('}'))
            {
                return "The template has an unbalanced { or } (a token looks like {<series>}).";
            }

            return null;
        }
        catch (NotSupportedException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>What the template produces for a made-up issue, for the live preview; falls back to the error text if it can't be evaluated.</summary>
    public static string Preview(string template) => Validate(template) ?? FormatPath(template, SampleSeries, SampleIssue, "Spawn 263 (2016).cbr", ".cbz");

    private static readonly WatchedSeries SampleSeries = new() { Name = "Spawn", Publisher = "Image", StartYear = 1992 };

    private static readonly WantedIssue SampleIssue = new() { IssueNumber = "263", Name = "Endgame", StoreDate = new DateTime(2016, 5, 4) };

    private static (Issue Issue, TemplateContext Context) Build(WatchedSeries watched, WantedIssue wanted, string sourcePath)
    {
        var issue = new Issue
        {
            Series = new Series { Name = watched.Name },
            Publisher = watched.Publisher,
            Title = wanted.Name,
            Volume = watched.StartYear is int y ? $"v{y}" : null,
            Number = wanted.IssueNumber,
            Year = wanted.StoreDate?.Year,
            Month = wanted.StoreDate?.Month,
            Day = wanted.StoreDate?.Day,
        };

        var context = new TemplateContext
        {
            Extra = new Dictionary<string, string?>
            {
                ["volumeyear"] = watched.StartYear?.ToString(CultureInfo.InvariantCulture),
                ["filename"] = Path.GetFileNameWithoutExtension(sourcePath),
            },
        };

        return (issue, context);
    }

    /// <summary>Evaluated template text -> relative path with sanitized, Windows-safe segments and the file extension on the last one.</summary>
    private static string ToPath(string evaluated, string extension)
    {
        var segments = evaluated.Split('/', '\\')
            .Select(segment => MakeSafe(Sanitizer.SanitizeSegment(segment)))
            .Where(segment => segment.Length > 0)
            .ToList();

        if (segments.Count == 0)
        {
            segments.Add("Unnamed");
        }

        segments[^1] += extension.StartsWith('.') || extension.Length == 0 ? extension : "." + extension;
        return string.Join('/', segments);
    }

    /// <summary>Windows safety on top of the CE sanitizer: no trailing dots/spaces, no reserved device names, bounded length.</summary>
    public static string MakeSafe(string segment)
    {
        var text = segment.Trim().TrimEnd('.', ' ');
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var stem = text.Split('.')[0].ToUpperInvariant();
        if (Reserved.Contains(stem))
        {
            text = "_" + text;
        }

        return text.Length > 120 ? text[..120].TrimEnd('.', ' ') : text;
    }

    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };
}
