using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.Data.Metadata;

/// <summary>One issue in a saved provider arc list (the persisted subset of <see cref="ArcIssue"/>).</summary>
public sealed record EventArcEntry(string Series, string Number, int Year, string? CoverDate, string? StoreDate, string? CoverImageUrl,
    string? Title, string? ComicVineIssueId, string? ComicVineSeriesId, string? MetronIssueId, string? MetronSeriesId);

/// <summary>A saved provider list, read back for the map.</summary>
public sealed record EventArcList(int StoryEventId, string Provider, string ArcId, DateTime FetchedAt, bool IsComplete, IReadOnlyList<EventArcEntry> Entries);

/// <summary>
/// Saves and loads <see cref="EventArcSnapshot"/> rows (docs/superpowers/specs/2026-11-11-continuity-map-completeness-design.md §1). Callers
/// only pass lists fetched under the event's own verified arc id (the stored Metron / ComicVine arc id); nothing here decides identity.
/// </summary>
public static class EventArcSnapshots
{
    public const string Metron = "Metron";
    public const string ComicVine = "ComicVine";

    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    /// <summary>Replaces the event's list for <paramref name="provider"/> (one row per event and provider) and saves.</summary>
    public static EventArcSnapshot Save(PaperbunkrDbContext context, int storyEventId, string provider, string arcId, ArcEntryList list, DateTime? now = null)
    {
        var entries = list.Issues.Select(i => new EventArcEntry(i.Series, i.Number, i.Year, i.CoverDate, i.StoreDate, i.CoverImageUrl, i.Title,
            i.ComicVineIssueId, i.ComicVineSeriesId, i.MetronIssueId, i.MetronSeriesId)).ToList();
        var row = context.EventArcSnapshots.FirstOrDefault(s => s.StoryEventId == storyEventId && s.Provider == provider);
        if (row is null)
        {
            row = new EventArcSnapshot { StoryEventId = storyEventId, Provider = provider };
            context.EventArcSnapshots.Add(row);
        }

        row.ArcId = arcId;
        row.FetchedAt = now ?? DateTime.UtcNow;
        row.ReportedCount = list.ReportedCount;
        row.IsComplete = list.SkippedLines.Count == 0 && (list.ReportedCount is null || list.ReportedCount == list.Issues.Count);
        row.IssuesJson = JsonSerializer.Serialize(entries, Json);
        context.SaveChanges();
        return row;
    }

    /// <summary>The saved lists for these events, Metron first.</summary>
    public static IReadOnlyList<EventArcList> Load(PaperbunkrDbContext context, IReadOnlyCollection<int> storyEventIds)
    {
        if (storyEventIds.Count == 0)
        {
            return Array.Empty<EventArcList>();
        }

        return context.EventArcSnapshots.AsNoTracking()
            .Where(s => storyEventIds.Contains(s.StoryEventId))
            .ToList()
            .OrderBy(s => s.StoryEventId).ThenBy(s => s.Provider == Metron ? 0 : 1)
            .Select(s => new EventArcList(s.StoryEventId, s.Provider, s.ArcId, s.FetchedAt, s.IsComplete, Parse(s.IssuesJson)))
            .ToList();
    }

    /// <summary>
    /// An event merge keeps the survivor's list per provider unless the loser's is newer, in which case the loser's moves over
    /// (§1, "an event merge keeps the newer snapshot per provider"). Doesn't save.
    /// </summary>
    public static void Merge(PaperbunkrDbContext context, int survivorId, int loserId)
    {
        var lists = context.EventArcSnapshots.Where(s => s.StoryEventId == survivorId || s.StoryEventId == loserId).ToList();
        foreach (var provider in lists.GroupBy(s => s.Provider))
        {
            var newest = provider.OrderByDescending(s => s.FetchedAt).ThenBy(s => s.StoryEventId == survivorId ? 0 : 1).First();
            foreach (var other in provider.Where(s => !ReferenceEquals(s, newest)))
            {
                context.EventArcSnapshots.Remove(other);
            }

            newest.StoryEventId = survivorId;
        }
    }

    private static IReadOnlyList<EventArcEntry> Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<EventArcEntry>>(json, Json) ?? new List<EventArcEntry>();
        }
        catch (JsonException)
        {
            return Array.Empty<EventArcEntry>();
        }
    }
}
