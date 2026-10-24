using System.Text.Json;

namespace Paperbunkr.Data.ComicVine.Scraping;

/// <summary>
/// The last <see cref="Capacity"/> automatic cover checks, kept so Preferences can say what each Strictness level would have done with them
/// (docs/superpowers/specs/2026-10-21-scraper-organizer-c-automation-ux-design.md section 1). Measurements only - no comic, no names. One small
/// JSON file in the data folder, written at most every few seconds.
/// </summary>
public sealed class ScrapeDecisionHistory(string path)
{
    public const int Capacity = 500;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private readonly object _gate = new();
    private List<CoverCheck>? _checks;
    private DateTime _lastWriteUtc = DateTime.MinValue;

    /// <summary>The app's history; null (tests, tools) records nothing.</summary>
    public static ScrapeDecisionHistory? Shared { get; set; }

    public void Record(CoverCheck check)
    {
        lock (_gate)
        {
            var checks = Load();
            checks.Add(check);
            if (checks.Count > Capacity)
            {
                checks.RemoveRange(0, checks.Count - Capacity);
            }

            if (DateTime.UtcNow - _lastWriteUtc > TimeSpan.FromSeconds(5))
            {
                Write(checks);
            }
        }
    }

    /// <summary>Writes whatever is still only in memory (end of a batch).</summary>
    public void Flush()
    {
        lock (_gate)
        {
            if (_checks is not null)
            {
                Write(_checks);
            }
        }
    }

    public IReadOnlyList<CoverCheck> Checks()
    {
        lock (_gate)
        {
            return Load().ToList();
        }
    }

    /// <summary>How many of the recorded checks each level would have applied without asking.</summary>
    public static IReadOnlyDictionary<ScrapeStrictness, int> WouldApply(IReadOnlyList<CoverCheck> checks) =>
        Enum.GetValues<ScrapeStrictness>().ToDictionary(level => level, level => checks.Count(c => ConfidencePolicy.Passes(level, c)));

    private List<CoverCheck> Load()
    {
        if (_checks is not null)
        {
            return _checks;
        }

        try
        {
            _checks = File.Exists(path) ? JsonSerializer.Deserialize<List<CoverCheck>>(File.ReadAllText(path), Json) ?? new() : new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _checks = new();
        }

        return _checks;
    }

    private void Write(List<CoverCheck> checks)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(checks, Json));
            File.Move(temp, path, overwrite: true);
            _lastWriteUtc = DateTime.UtcNow;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
