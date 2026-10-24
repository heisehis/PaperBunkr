using System.Text.Json;

namespace Paperbunkr.Data.ComicVine.Scraping;

/// <summary>A batch scrape written down as it runs, so a cancel or a crash resumes from the last finished comic
/// (docs/superpowers/specs/2026-10-21-scraper-organizer-b-speed-core-design.md section 8).</summary>
public sealed class ScrapeJobRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>What started it: "Selection", "UnmatchedAll", "Scheduled", "Import".</summary>
    public string Kind { get; set; } = "Selection";

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public bool Interactive { get; set; }

    /// <summary>The comics still to do, in order.</summary>
    public List<int> Pending { get; set; } = new();

    public int Done { get; set; }

    public int Total { get; set; }
}

/// <summary>
/// One file per batch under <c>scrape-jobs/</c> in the data folder. A finished batch deletes its file; whatever is left at the next start
/// was cancelled or cut short and is offered for resuming. A file rather than a table: it is working state, not library data.
/// </summary>
public sealed class ScrapeJobStore(string folder)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    /// <summary>The app's store; null (tests, tools) means batches are not written down.</summary>
    public static ScrapeJobStore? Shared { get; set; }

    /// <summary>Only batches at least this long are written down: a short one is quicker to redo than to resume.</summary>
    public const int MinimumBooks = 10;

    public void Save(ScrapeJobRecord job)
    {
        try
        {
            Directory.CreateDirectory(folder);
            string path = PathFor(job.Id);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(job, Json));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not being able to resume is a lesser problem than failing the scrape.
        }
    }

    public void Delete(Guid id)
    {
        try
        {
            File.Delete(PathFor(id));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Batches that did not finish, newest first.</summary>
    public IReadOnlyList<ScrapeJobRecord> LoadUnfinished()
    {
        if (!Directory.Exists(folder))
        {
            return Array.Empty<ScrapeJobRecord>();
        }

        var jobs = new List<ScrapeJobRecord>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
        {
            try
            {
                var job = JsonSerializer.Deserialize<ScrapeJobRecord>(File.ReadAllText(file), Json);
                if (job is { Pending.Count: > 0 })
                {
                    jobs.Add(job);
                }
                else
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
            }
        }

        return jobs.OrderByDescending(j => j.CreatedUtc).ToList();
    }

    private string PathFor(Guid id) => Path.Combine(folder, id.ToString("N") + ".json");
}
