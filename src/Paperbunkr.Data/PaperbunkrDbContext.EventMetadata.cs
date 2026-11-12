using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata.EventEnrichment;

namespace Paperbunkr.Data;

public partial class PaperbunkrDbContext
{
    public DbSet<EventMetadataState> EventMetadataStates => Set<EventMetadataState>();
    public DbSet<EventDateEndpoint> EventDateEndpoints => Set<EventDateEndpoint>();
    public DbSet<EventMetadataChange> EventMetadataChanges => Set<EventMetadataChange>();
    internal bool EventMetadataWriteManaged { get; set; }

    private static void ConfigureEventMetadata(ModelBuilder model)
    {
        model.Entity<EventMetadataState>(b =>
        {
            b.HasKey(x => new { x.StoryEventId, x.Field });
            b.Property(x => x.Field).HasConversion<string>();
            b.Property(x => x.Revision).IsConcurrencyToken();
            b.HasOne(x => x.StoryEvent).WithMany(e => e.MetadataStates).HasForeignKey(x => x.StoryEventId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<EventDateEndpoint>(b =>
        {
            b.HasKey(x => new { x.StoryEventId, x.IsEnd });
            b.HasOne(x => x.StoryEvent).WithMany(e => e.DateEndpoints).HasForeignKey(x => x.StoryEventId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<EventMetadataChange>(b =>
        {
            b.HasKey(x => x.Id); b.Property(x => x.Field).HasConversion<string>();
            b.HasIndex(x => new { x.StoryEventId, x.CreatedAt }); b.HasIndex(x => x.OperationId);
            b.HasOne(x => x.StoryEvent).WithMany(e => e.MetadataChanges).HasForeignKey(x => x.StoryEventId).OnDelete(DeleteBehavior.Cascade);
        });
        // Date endpoints are small and must accompany all tracked event reads, including legacy callers.
        model.Entity<StoryEvent>().Navigation(e => e.DateEndpoints).AutoInclude();
        model.Entity<StoryEvent>().Navigation(e => e.MetadataStates).AutoInclude();
    }

    private void PrepareEventMetadataWrites()
    {
        if (EventMetadataWriteManaged) return;
        ChangeTracker.DetectChanges();
        var fields = new HashSet<(int Id, EventMetadataField Field)>();
        foreach (var entry in ChangeTracker.Entries<StoryEvent>().Where(x => x.State == EntityState.Modified).ToArray())
        {
            foreach (var (property, field) in new[]
            {
                (nameof(StoryEvent.Name), EventMetadataField.Name), (nameof(StoryEvent.Description), EventMetadataField.Description),
                (nameof(StoryEvent.CoverImageUrl), EventMetadataField.Cover), (nameof(StoryEvent.CoverSource), EventMetadataField.Cover),
                (nameof(StoryEvent.StartDate), EventMetadataField.StartDate), (nameof(StoryEvent.EndDate), EventMetadataField.EndDate),
            })
                if (entry.Property(property).IsModified)
                {
                    fields.Add((entry.Entity.Id, field));
                    if (field is EventMetadataField.StartDate or EventMetadataField.EndDate)
                    {
                        bool end = field == EventMetadataField.EndDate;
                        var endpoint = entry.Entity.DateEndpoints.SingleOrDefault(d => d.IsEnd == end);
                        if (endpoint is not null && Entry(endpoint).State == EntityState.Unchanged)
                        {
                            var exact = end ? entry.Entity.EndDate : entry.Entity.StartDate;
                            EventMetadataStore.SetDate(this, entry.Entity, end, exact is DateTime d ? new(d.Year, d.Month, d.Day) : null);
                        }
                    }
                }
        }
        foreach (var entry in ChangeTracker.Entries<EventDateEndpoint>().Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray())
            fields.Add((entry.Entity.StoryEventId, entry.Entity.IsEnd ? EventMetadataField.EndDate : EventMetadataField.StartDate));
        foreach (var entry in ChangeTracker.Entries<StoryEventTypeAssignment>().Where(x => x.State is EntityState.Added or EntityState.Deleted).ToArray())
            fields.Add((entry.Entity.StoryEventId, EventMetadataField.Types));
        foreach (var entry in ChangeTracker.Entries<StoryEventTag>().Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray())
            fields.Add((entry.Entity.StoryEventId, EventMetadataField.Tags));

        foreach (var (id, field) in fields.Where(x => x.Id > 0))
        {
            var e = EventMetadataStore.Query(this).SingleOrDefault(e => e.Id == id);
            if (e is null || Entry(e).State == EntityState.Deleted) continue;
            var state = EventMetadataStore.State(e, field);
            // A different revision on every saved edit prevents equal-value/ABA edits from fooling Undo.
            state.Revision = Guid.NewGuid().ToString("N"); state.EvidenceJson = null;
            state.ProtectedBlank = EventMetadataStore.Value(this, e, field) is null;
        }
    }
}
