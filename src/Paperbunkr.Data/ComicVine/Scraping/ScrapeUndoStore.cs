using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.ComicVine.Scraping;

/// <summary>One scrape batch's "before" picture: the scrape-writable fields of each comic it changed.</summary>
public sealed class ScrapeUndoBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public string Label { get; set; } = string.Empty;

    /// <summary>Issue id -> field name -> the value before the scrape (as JSON).</summary>
    public Dictionary<int, Dictionary<string, JsonElement>> Before { get; set; } = new();
}

/// <summary>
/// "Undo this batch" (docs/superpowers/specs/2026-10-21-scraper-organizer-c-automation-ux-design.md section 3). Before a batch applies anything,
/// the fields a scrape can write are copied for every comic in it; afterwards only the comics that were actually changed are kept, in one file
/// per batch under <c>scrape-undo/</c>. Undo puts those values back. Batches older than <see cref="KeepFor"/> are pruned.
/// <para>
/// What it restores: the comic's own metadata fields (<see cref="Fields"/>). Not the series' name or count, which a scrape can also touch and
/// other comics share, and not the metadata-model records a scrape adds alongside (credits, links); those stay as the scrape left them.
/// </para>
/// </summary>
public sealed class ScrapeUndoStore(string folder)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(30);

    /// <summary>The app's store; null (tests, tools) means batches can't be undone.</summary>
    public static ScrapeUndoStore? Shared { get; set; }

    /// <summary>Every <see cref="Issue"/> field the scraper writes (IssueDetailsApplier and the volume-level apply).</summary>
    public static readonly IReadOnlyList<string> Fields = new[]
    {
        nameof(Issue.Title), nameof(Issue.Number), nameof(Issue.Count), nameof(Issue.Volume), nameof(Issue.MetadataSource),
        nameof(Issue.AlternateNumber), nameof(Issue.AlternateCount), nameof(Issue.StoryArc), nameof(Issue.StoryArcNumber), nameof(Issue.SeriesGroup),
        nameof(Issue.Summary), nameof(Issue.Year), nameof(Issue.Month), nameof(Issue.Day), nameof(Issue.ReleasedTime),
        nameof(Issue.Writer), nameof(Issue.Penciller), nameof(Issue.Inker), nameof(Issue.Colorist), nameof(Issue.Letterer), nameof(Issue.CoverArtist),
        nameof(Issue.Editor), nameof(Issue.Translator), nameof(Issue.Publisher), nameof(Issue.Imprint), nameof(Issue.Upc), nameof(Issue.ISBN),
        nameof(Issue.Web), nameof(Issue.AgeRating), nameof(Issue.Characters), nameof(Issue.Teams), nameof(Issue.Locations),
        nameof(Issue.MainCharacterOrTeam), nameof(Issue.CommunityRating), nameof(Issue.CommunityRatingCount), nameof(Issue.GcdIssueId),
    };

    private static readonly IReadOnlyList<PropertyInfo> Properties =
        Fields.Select(f => typeof(Issue).GetProperty(f)!).ToList();

    /// <summary>The "before" picture of <paramref name="issueIds"/>, not yet saved.</summary>
    public static ScrapeUndoBatch Capture(PaperbunkrDbContext context, IReadOnlyCollection<int> issueIds, string label)
    {
        var batch = new ScrapeUndoBatch { Label = label };
        foreach (var issue in context.Issues.AsNoTracking().Where(i => issueIds.Contains(i.Id)))
        {
            batch.Before[issue.Id] = Properties.ToDictionary(p => p.Name, p => JsonSerializer.SerializeToElement(p.GetValue(issue), p.PropertyType, Json));
        }

        return batch;
    }

    /// <summary>Keeps only <paramref name="changedIds"/> and saves the batch; nothing is saved when none changed.</summary>
    public void Save(ScrapeUndoBatch batch, IEnumerable<int> changedIds)
    {
        var keep = changedIds.ToHashSet();
        foreach (var id in batch.Before.Keys.Where(id => !keep.Contains(id)).ToList())
        {
            batch.Before.Remove(id);
        }

        if (batch.Before.Count == 0)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(PathFor(batch.Id), JsonSerializer.Serialize(batch, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Saved batches, newest first, pruning the expired ones on the way.</summary>
    public IReadOnlyList<ScrapeUndoBatch> List(DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        if (!Directory.Exists(folder))
        {
            return Array.Empty<ScrapeUndoBatch>();
        }

        var batches = new List<ScrapeUndoBatch>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
        {
            try
            {
                var batch = JsonSerializer.Deserialize<ScrapeUndoBatch>(File.ReadAllText(file), Json);
                if (batch is null || now - batch.CreatedUtc > KeepFor)
                {
                    File.Delete(file);
                    continue;
                }

                batches.Add(batch);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
            }
        }

        return batches.OrderByDescending(b => b.CreatedUtc).ToList();
    }

    /// <summary>Puts every field back as it was before the batch, deletes the batch, and returns the comics it touched (to write back to their files).</summary>
    public IReadOnlyList<int> Undo(PaperbunkrDbContext context, ScrapeUndoBatch batch)
    {
        var ids = batch.Before.Keys.ToList();
        var restored = new List<int>();
        foreach (var issue in context.Issues.Where(i => ids.Contains(i.Id)))
        {
            foreach (var property in Properties)
            {
                if (batch.Before[issue.Id].TryGetValue(property.Name, out var value))
                {
                    property.SetValue(issue, value.Deserialize(property.PropertyType, Json));
                }
            }

            restored.Add(issue.Id);
        }

        context.SaveChanges();
        try
        {
            File.Delete(PathFor(batch.Id));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return restored;
    }

    private string PathFor(Guid id) => Path.Combine(folder, id.ToString("N") + ".json");
}
