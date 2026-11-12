using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata.EventEnrichment;

public sealed record EventCoverValue(string? Url, StoryEventCoverSource Source);
public sealed record EventTagValue(string Value, string Category, IssueTagWeight Weight);
public sealed record EventFieldSnapshot(string? Value, string Revision, bool Protected);
public sealed record EventMetadataSnapshot(int EventId, string Identity, Dictionary<EventMetadataField, EventFieldSnapshot> Fields);

public static class EventMetadataStore
{
    public static IQueryable<StoryEvent> Query(PaperbunkrDbContext db) => db.StoryEvents
        .Include(e => e.MetadataStates).Include(e => e.DateEndpoints).Include(e => e.TypeAssignments)
        .Include(e => e.Tags).Include(e => e.Members).AsSplitQuery();

    public static string Identity(StoryEvent e) => $"{e.MetronArcId}|{e.ComicVineArcId}|{e.GcdArcId}|{e.IdentityConflict}|"
        + string.Join(',', e.Members.Select(m => m.IssueId).Order());

    public static EventMetadataSnapshot Capture(PaperbunkrDbContext db, StoryEvent e) => new(e.Id, Identity(e),
        Enum.GetValues<EventMetadataField>().ToDictionary(f => f, f =>
        {
            var state = e.MetadataStates.SingleOrDefault(s => s.Field == f);
            return new EventFieldSnapshot(Value(db, e, f), state?.Revision ?? "legacy", state?.ProtectedBlank ?? false);
        }));

    public static string? Value(PaperbunkrDbContext db, StoryEvent e, EventMetadataField field) => field switch
    {
        EventMetadataField.Name => Blank(e.Name),
        EventMetadataField.Description => Blank(e.Description),
        EventMetadataField.Cover => e.CoverSource == StoryEventCoverSource.None && Blank(e.CoverImageUrl) is null ? null
            : JsonSerializer.Serialize(new EventCoverValue(e.CoverImageUrl, e.CoverSource)),
        EventMetadataField.StartDate or EventMetadataField.EndDate => EventPublicationDate.Effective(e, field == EventMetadataField.EndDate) is { } date
            ? JsonSerializer.Serialize(date) : null,
        EventMetadataField.Types => ArrayValue(e.TypeAssignments.Where(t => db.Entry(t).State != EntityState.Deleted)
            .Select(t => t.Type).Distinct().Order().ToArray()),
        EventMetadataField.Tags => ArrayValue(e.Tags.Where(t => db.Entry(t).State != EntityState.Deleted)
            .Select(t => new EventTagValue(t.Value, t.Category, t.Weight)).OrderBy(t => t.Category).ThenBy(t => t.Value).ToArray()),
        _ => throw new ArgumentOutOfRangeException(nameof(field)),
    };

    private static string? ArrayValue<T>(T[] values) => values.Length == 0 ? null : JsonSerializer.Serialize(values);
    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    public static EventMetadataState State(StoryEvent e, EventMetadataField field)
    {
        var row = e.MetadataStates.SingleOrDefault(s => s.Field == field);
        if (row is null) e.MetadataStates.Add(row = new() { StoryEvent = e, Field = field });
        return row;
    }

    public static bool Eligible(PaperbunkrDbContext db, StoryEvent e, EventMetadataField field) =>
        Value(db, e, field) is null && !e.MetadataStates.Any(s => s.Field == field && s.ProtectedBlank);

    public static void Set(PaperbunkrDbContext db, StoryEvent e, EventMetadataField field, string? value)
    {
        switch (field)
        {
            case EventMetadataField.Name: e.Name = value ?? string.Empty; break;
            case EventMetadataField.Description: e.Description = value; break;
            case EventMetadataField.Cover:
                var cover = value is null ? null : JsonSerializer.Deserialize<EventCoverValue>(value);
                e.CoverImageUrl = cover?.Url; e.CoverSource = cover?.Source ?? StoryEventCoverSource.None; break;
            case EventMetadataField.StartDate:
            case EventMetadataField.EndDate:
                SetDate(db, e, field == EventMetadataField.EndDate, value is null ? null : JsonSerializer.Deserialize<EventPublicationDate>(value)); break;
            case EventMetadataField.Types:
                var desiredTypes = value is null ? [] : JsonSerializer.Deserialize<StoryEventType[]>(value)!;
                foreach (var old in e.TypeAssignments.Where(t => !desiredTypes.Contains(t.Type)).ToArray())
                { db.StoryEventTypeAssignments.Remove(old); e.TypeAssignments.Remove(old); }
                foreach (var type in desiredTypes.Where(t => e.TypeAssignments.All(old => old.Type != t)))
                    e.TypeAssignments.Add(new() { StoryEvent = e, Type = type });
                break;
            case EventMetadataField.Tags:
                var desiredTags = value is null ? [] : JsonSerializer.Deserialize<EventTagValue[]>(value)!;
                foreach (var old in e.Tags.Where(t => desiredTags.All(d => d.Value != t.Value || d.Category != t.Category)).ToArray())
                { db.StoryEventTags.Remove(old); e.Tags.Remove(old); }
                foreach (var tag in desiredTags)
                {
                    var old = e.Tags.SingleOrDefault(t => t.Value == tag.Value && t.Category == tag.Category);
                    if (old is null) e.Tags.Add(new() { StoryEvent = e, Value = tag.Value, Category = tag.Category, Weight = tag.Weight });
                    else old.Weight = tag.Weight;
                }
                break;
        }
    }

    public static void SetDate(PaperbunkrDbContext db, StoryEvent e, bool end, EventPublicationDate? date)
    {
        var row = e.DateEndpoints.SingleOrDefault(d => d.IsEnd == end);
        if (date is null)
        {
            if (row is not null) { db.EventDateEndpoints.Remove(row); e.DateEndpoints.Remove(row); }
        }
        else
        {
            if (row is null) e.DateEndpoints.Add(row = new() { StoryEvent = e, IsEnd = end });
            row.Year = date.Year; row.Month = date.Month; row.Day = date.Day;
            row.Uncertain = date.Uncertain; row.Provisional = date.Provisional; row.Basis = date.Basis; row.Coverage = date.Coverage;
        }
        if (end) e.EndDate = date?.Exact; else e.StartDate = date?.Exact;
    }

    public static string Display(EventMetadataField field, string? value)
    {
        if (value is null) return "Empty";
        try
        {
            return field switch
            {
                EventMetadataField.StartDate => JsonSerializer.Deserialize<EventPublicationDate>(value) is { } first ? first.Entry + (first.Provisional ? " (provisional)" : "") + (first.Uncertain ? " (uncertain)" : "") : "Empty",
                EventMetadataField.EndDate => JsonSerializer.Deserialize<EventPublicationDate>(value)!.Display,
                EventMetadataField.Cover => JsonSerializer.Deserialize<EventCoverValue>(value) is { } cover ? $"{cover.Source}: {cover.Url ?? "Custom cover"}" : "Empty",
                EventMetadataField.Types => string.Join(", ", JsonSerializer.Deserialize<StoryEventType[]>(value)!.Select(t => t switch
                { StoryEventType.StoryArc => "Story Arc", StoryEventType.LimitedSeries => "Limited Series", StoryEventType.OneShotOrSpecial => "One-shot / Special", _ => t.ToString() })),
                EventMetadataField.Tags => string.Join(", ", JsonSerializer.Deserialize<EventTagValue[]>(value)!.Select(t => t.Value)),
                _ => value,
            };
        }
        catch (JsonException) { return value; }
    }

    public static string FieldLabel(EventMetadataField field) => field switch
    {
        EventMetadataField.StartDate => "Start date", EventMetadataField.EndDate => "End date",
        EventMetadataField.Types => "Event types", EventMetadataField.Tags => "Event tags", _ => field.ToString(),
    };
}
