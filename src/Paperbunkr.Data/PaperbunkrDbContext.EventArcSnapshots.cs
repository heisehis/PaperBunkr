using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data;

public partial class PaperbunkrDbContext
{
    /// <summary>Provider issue lists per event (docs/superpowers/specs/2026-11-11-continuity-map-completeness-design.md §1).</summary>
    public DbSet<EventArcSnapshot> EventArcSnapshots => Set<EventArcSnapshot>();

    private static void ConfigureEventArcSnapshots(ModelBuilder model)
    {
        model.Entity<EventArcSnapshot>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Provider).IsRequired().HasMaxLength(16);
            b.Property(x => x.ArcId).IsRequired().HasMaxLength(64);
            b.Property(x => x.IssuesJson).IsRequired();
            b.HasIndex(x => new { x.StoryEventId, x.Provider }).IsUnique();
            b.HasOne(x => x.StoryEvent).WithMany().HasForeignKey(x => x.StoryEventId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
