using System.Text.Json;

namespace Paperbunkr.Data.ComicVine.Scraping;

/// <summary>One comic an unattended scrape could not match with confidence.</summary>
public sealed record ScrapeReviewItem(int IssueId, string BookLabel, string? Reason, DateTime AddedUtc);

/// <summary>
/// The comics waiting for a person to pick their match (docs/superpowers/specs/2026-10-21-scraper-organizer-c-automation-ux-design.md
/// section 2): unattended runs (scheduled, scrape-on-import, "Scrape all unmatched", the ComicVine fallback) never ask, so what they
/// can't confirm lands here instead of being silently dropped. One small JSON file in the data folder.
/// </summary>
public sealed class ScrapeReviewQueue(string path)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private readonly object _gate = new();

    /// <summary>The app's queue; null (tests, tools) means nothing is queued.</summary>
    public static ScrapeReviewQueue? Shared { get; set; }

    public event EventHandler? Changed;

    public IReadOnlyList<ScrapeReviewItem> Load()
    {
        lock (_gate)
        {
            return Read();
        }
    }

    /// <summary>Adds (or refreshes) items; one entry per comic.</summary>
    public void Add(IEnumerable<ScrapeReviewItem> items)
    {
        lock (_gate)
        {
            var byId = Read().ToDictionary(i => i.IssueId);
            foreach (var item in items)
            {
                byId[item.IssueId] = item;
            }

            Write(byId.Values.OrderBy(i => i.AddedUtc).ToList());
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(IEnumerable<int> issueIds)
    {
        var ids = issueIds.ToHashSet();
        lock (_gate)
        {
            Write(Read().Where(i => !ids.Contains(i.IssueId)).ToList());
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private List<ScrapeReviewItem> Read()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<List<ScrapeReviewItem>>(File.ReadAllText(path), Json) ?? new() : new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    private void Write(List<ScrapeReviewItem> items)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(items, Json));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
