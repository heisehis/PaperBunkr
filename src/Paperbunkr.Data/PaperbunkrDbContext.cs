using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data;

/// <summary>
/// EF Core context for Paperbunkr's SQLite library database (docs/onboarding.md §5). Replaces
/// CE's single whole-library <c>ComicDb.xml</c> (fully loaded/rewritten on every save).
/// </summary>
public class PaperbunkrDbContext : DbContext
{
    public DbSet<Series> Series => Set<Series>();

    public DbSet<Issue> Issues => Set<Issue>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<TrackingLink> TrackingLinks => Set<TrackingLink>();

    public DbSet<IssueCustomValue> IssueCustomValues => Set<IssueCustomValue>();

    public DbSet<SmartList> SmartLists => Set<SmartList>();

    public DbSet<SmartListCondition> SmartListConditions => Set<SmartListCondition>();

    public DbSet<ReadingList> ReadingLists => Set<ReadingList>();

    public DbSet<ReadingListItem> ReadingListItems => Set<ReadingListItem>();

    public DbSet<SeriesConflict> SeriesConflicts => Set<SeriesConflict>();

    public DbSet<AppSettings> AppSettings => Set<AppSettings>();

    public DbSet<VirtualTagDefinition> VirtualTagDefinitions => Set<VirtualTagDefinition>();

    public DbSet<WatchedFolder> WatchedFolders => Set<WatchedFolder>();

    public PaperbunkrDbContext(DbContextOptions<PaperbunkrDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Returns the singleton <see cref="Entities.AppSettings"/> row (<c>Id</c> always 1), creating
    /// it on first access - mirrors how <c>PaperbunkrDb.EnsureCreated</c> seeds the system smart
    /// lists idempotently.
    /// </summary>
    public AppSettings GetOrCreateAppSettings()
    {
        var settings = AppSettings.FirstOrDefault(a => a.Id == 1);
        if (settings is null)
        {
            settings = new AppSettings();
            AppSettings.Add(settings);
            SaveChanges();
        }

        return settings;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Enum storage choice: store as their string name (via HasConversion<string>()), not the
        // underlying int. Costs a few bytes per row but keeps `sqlite3 paperbunkr.db` / ad-hoc
        // queries human-readable, and — more importantly — insulates the stored data from enum
        // member reordering (an int-backed enum silently corrupts existing rows if a value is
        // ever inserted/reordered rather than appended; a string-backed one just needs a rename
        // migration, which is visible and deliberate). Applied consistently to every enum below.
        modelBuilder.Entity<Series>(builder =>
        {
            builder.HasKey(s => s.Id);
            builder.Property(s => s.Name).IsRequired();
            builder.Property(s => s.ContentType).HasConversion<string>().HasMaxLength(32);
            builder.Property(s => s.ReadingMode).HasConversion<string>().HasMaxLength(32);

            builder.HasIndex(s => s.Name);

            builder.HasMany(s => s.Issues)
                .WithOne(i => i.Series)
                .HasForeignKey(i => i.SeriesId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(s => s.TrackingLinks)
                .WithOne(t => t.Series)
                .HasForeignKey(t => t.SeriesId)
                .OnDelete(DeleteBehavior.Cascade);

            // Series.CoverIssueId -> Issue is a same-aggregate reference to one of its own Issues.
            // Modeled as an independent FK (not part of the Series/Issues collection navigation)
            // with Restrict delete behavior so deleting the cover issue can't cascade into
            // deleting the whole series; callers must clear CoverIssueId first.
            builder.HasOne(s => s.CoverIssue)
                .WithMany()
                .HasForeignKey(s => s.CoverIssueId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasMany(s => s.Categories)
                .WithMany(c => c.Series);
        });

        modelBuilder.Entity<Issue>(builder =>
        {
            builder.HasKey(i => i.Id);
            builder.Property(i => i.ReadingModeOverride).HasConversion<string>().HasMaxLength(32);
            builder.HasIndex(i => i.SeriesId);
            builder.HasIndex(i => i.FilePath);

            builder.HasMany(i => i.CustomValues)
                .WithOne(cv => cv.Issue)
                .HasForeignKey(cv => cv.IssueId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Category>(builder =>
        {
            builder.HasKey(c => c.Id);
            builder.Property(c => c.Name).IsRequired();
        });

        modelBuilder.Entity<TrackingLink>(builder =>
        {
            builder.HasKey(t => t.Id);
            builder.Property(t => t.Service).HasConversion<string>().HasMaxLength(32);
            builder.Property(t => t.ExternalId).IsRequired();
            builder.HasIndex(t => new { t.SeriesId, t.Service, t.ExternalId }).IsUnique();
        });

        modelBuilder.Entity<IssueCustomValue>(builder =>
        {
            builder.HasKey(cv => cv.Id);
            builder.Property(cv => cv.Name).IsRequired();
            builder.HasIndex(cv => new { cv.IssueId, cv.Name });
        });

        modelBuilder.Entity<SmartList>(builder =>
        {
            builder.HasKey(s => s.Id);
            builder.Property(s => s.Name).IsRequired();

            builder.HasMany(s => s.Conditions)
                .WithOne(c => c.SmartList)
                .HasForeignKey(c => c.SmartListId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SmartListCondition>(builder =>
        {
            builder.HasKey(c => c.Id);
            builder.Property(c => c.Field).HasConversion<string>().HasMaxLength(32);
            builder.Property(c => c.Operator).HasConversion<string>().HasMaxLength(32);
            builder.HasIndex(c => c.SmartListId);
        });

        modelBuilder.Entity<ReadingList>(builder =>
        {
            builder.HasKey(r => r.Id);
            builder.Property(r => r.Name).IsRequired();

            builder.HasMany(r => r.Items)
                .WithOne(i => i.ReadingList)
                .HasForeignKey(i => i.ReadingListId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReadingListItem>(builder =>
        {
            builder.HasKey(i => i.Id);
            builder.HasIndex(i => i.ReadingListId);

            // An Issue can appear in many reading lists (and more than once within the same
            // list, e.g. a crossover issue revisited later) - Restrict, not Cascade, so deleting
            // an Issue can't silently cascade-delete unrelated reading lists; callers must remove
            // the item explicitly first.
            builder.HasOne(i => i.Issue)
                .WithMany()
                .HasForeignKey(i => i.IssueId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SeriesConflict>(builder =>
        {
            builder.HasKey(c => c.Id);
            builder.Property(c => c.IncomingName).IsRequired();
            builder.Property(c => c.MatchedName).IsRequired();
            builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(32);
            builder.HasIndex(c => c.Status);

            // All three Series references are optional (see SeriesConflict's doc comment) and
            // informational only - SetNull so a conflict row survives (with a dangling reference
            // cleared) if the series it points at is later deleted through some other path, rather
            // than blocking that delete with an FK violation.
            builder.HasOne(c => c.ExistingSeries)
                .WithMany()
                .HasForeignKey(c => c.ExistingSeriesId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasOne(c => c.SeriesA)
                .WithMany()
                .HasForeignKey(c => c.SeriesAId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasOne(c => c.SeriesB)
                .WithMany()
                .HasForeignKey(c => c.SeriesBId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Singleton row (Id always 1) - one app-wide config record, not a generic key-value store.
        modelBuilder.Entity<AppSettings>(builder =>
        {
            builder.HasKey(a => a.Id);
            builder.Property(a => a.ActiveSkinKey).IsRequired().HasDefaultValue("default");
            builder.Property(a => a.OpenLastPage).HasDefaultValue(true);
            builder.Property(a => a.AutoNavigateComics).HasDefaultValue(true);
            builder.Property(a => a.BackupsToKeep).HasDefaultValue(5);
            builder.Property(a => a.ReverseRtlNavigation).HasDefaultValue(true);
            builder.Property(a => a.HighQualityPageDisplay).HasDefaultValue(true);
        });

        modelBuilder.Entity<VirtualTagDefinition>(builder =>
        {
            builder.HasKey(v => v.Id);
            builder.Property(v => v.Name).IsRequired();
        });

        modelBuilder.Entity<WatchedFolder>(builder =>
        {
            builder.HasKey(w => w.Id);
            builder.Property(w => w.Path).IsRequired();
            builder.HasIndex(w => w.Path).IsUnique();
        });
    }

    /// <summary>
    /// Test-only redirect for <see cref="GetDefaultDatabasePath"/> - mutable so tests can point
    /// every <c>PaperbunkrDb.CreateContext()</c> call (App-side ViewModels have no injected
    /// context-factory seam, unlike <c>SkinService</c>/<c>CoverThumbnailService</c>) at a temp
    /// SQLite file instead of the real per-user database. Never set this outside a test's own
    /// constructor/teardown.
    /// </summary>
    public static string? DatabasePathOverride { get; set; }

    /// <summary>Default SQLite file location convention: %AppData%\Paperbunkr\paperbunkr.db.</summary>
    public static string GetDefaultDatabasePath()
    {
        if (DatabasePathOverride is not null)
        {
            return DatabasePathOverride;
        }

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string dir = Path.Combine(appData, "Paperbunkr");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "paperbunkr.db");
    }
}
