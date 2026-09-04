using System.Linq;
using System.Threading;
using Microsoft.Data.Sqlite;
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

    public DbSet<Collection> Collections => Set<Collection>();

    public DbSet<CollectionItem> CollectionItems => Set<CollectionItem>();

    public DbSet<CollectionRelation> CollectionRelations => Set<CollectionRelation>();

    public DbSet<TrackingLink> TrackingLinks => Set<TrackingLink>();

    public DbSet<IssueCustomValue> IssueCustomValues => Set<IssueCustomValue>();

    public DbSet<IssueBookmark> IssueBookmarks => Set<IssueBookmark>();

    public DbSet<IssuePage> IssuePages => Set<IssuePage>();

    public DbSet<IssueTag> IssueTags => Set<IssueTag>();

    public DbSet<SeriesTitle> SeriesTitles => Set<SeriesTitle>();

    public DbSet<SmartList> SmartLists => Set<SmartList>();

    public DbSet<SmartListCondition> SmartListConditions => Set<SmartListCondition>();

    public DbSet<SmartListConditionGroup> SmartListConditionGroups => Set<SmartListConditionGroup>();

    public DbSet<ReadingList> ReadingLists => Set<ReadingList>();

    public DbSet<ReadingListItem> ReadingListItems => Set<ReadingListItem>();

    public DbSet<ReadingListTag> ReadingListTags => Set<ReadingListTag>();

    public DbSet<SeriesConflict> SeriesConflicts => Set<SeriesConflict>();

    public DbSet<MetadataProposal> MetadataProposals => Set<MetadataProposal>();

    public DbSet<MediaRelation> MediaRelations => Set<MediaRelation>();

    public DbSet<RelationEvidence> RelationEvidence => Set<RelationEvidence>();

    public DbSet<Continuity> Continuities => Set<Continuity>();

    public DbSet<ContinuityMembership> ContinuityMemberships => Set<ContinuityMembership>();

    public DbSet<StoryEvent> StoryEvents => Set<StoryEvent>();

    public DbSet<EventMembership> EventMemberships => Set<EventMembership>();

    public DbSet<EventRelation> EventRelations => Set<EventRelation>();

    public DbSet<EventRelationEvidence> EventRelationEvidence => Set<EventRelationEvidence>();

    public DbSet<EventSuggestionDismissal> EventSuggestionDismissals => Set<EventSuggestionDismissal>();

    public DbSet<Character> Characters => Set<Character>();

    public DbSet<CharacterAppearance> CharacterAppearances => Set<CharacterAppearance>();

    public DbSet<ExternalMediaId> ExternalMediaIds => Set<ExternalMediaId>();

    public DbSet<ExternalMetadataSnapshot> ExternalMetadataSnapshots => Set<ExternalMetadataSnapshot>();

    public DbSet<ExternalRating> ExternalRatings => Set<ExternalRating>();

    public DbSet<AppSettings> AppSettings => Set<AppSettings>();

    public DbSet<ProviderCredential> ProviderCredentials => Set<ProviderCredential>();

    public DbSet<VirtualTagDefinition> VirtualTagDefinitions => Set<VirtualTagDefinition>();

    public DbSet<WatchedFolder> WatchedFolders => Set<WatchedFolder>();

    public DbSet<KeyBinding> KeyBindings => Set<KeyBinding>();

    public DbSet<Workspace> Workspaces => Set<Workspace>();

    public DbSet<BookSeries> BookSeries => Set<BookSeries>();

    public DbSet<Book> Books => Set<Book>();

    public DbSet<BookBookmark> BookBookmarks => Set<BookBookmark>();

    public DbSet<BookHighlight> BookHighlights => Set<BookHighlight>();

    public DbSet<BookAnnotationImage> BookAnnotationImages => Set<BookAnnotationImage>();

    public DbSet<BookFolder> BookFolders => Set<BookFolder>();

    public DbSet<PluginCommandState> PluginCommandStates => Set<PluginCommandState>();

    public DbSet<PluginSettingState> PluginSettingStates => Set<PluginSettingState>();

    public DbSet<ActivityRun> ActivityRuns => Set<ActivityRun>();

    public PaperbunkrDbContext(DbContextOptions<PaperbunkrDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Retries on SQLite's own transient lock errors (BUSY=5, LOCKED=6) - each call site in this
    /// app opens a fresh short-lived context/connection (<c>PaperbunkrDb.CreateContext</c>'s own
    /// doc comment), so two of them can legitimately race against each other (e.g. the Library
    /// screen's per-keystroke settings save landing while a background scan or the live-folder
    /// watcher is also writing). Microsoft.Data.Sqlite already retries internally via its own
    /// 30-second default busy timeout, but that alone wasn't enough to prevent a real "database is
    /// locked" `DbUpdateException` surfacing to the user - this adds a small additional retry at
    /// the EF layer rather than changing that timeout, which is already generous. Every other
    /// <see cref="SqliteException"/> (constraint violations, corruption, etc.) rethrows immediately
    /// on the first attempt, same as before this override existed.
    /// </summary>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return base.SaveChanges(acceptAllChangesOnSuccess);
            }
            catch (DbUpdateException ex) when (attempt < maxAttempts && IsTransientLockError(ex))
            {
                Thread.Sleep(attempt * 150);
            }
        }
    }

    /// <summary>
    /// Exposed for callers that persist a non-critical, easily-recomputed value (e.g. a UI display
    /// preference) and would rather silently skip that one save than surface a crash for it - see
    /// <c>LibraryScreenViewModel.SaveLibrarySettings</c>, called on every Library search keystroke.
    /// </summary>
    public static bool IsTransientLockError(DbUpdateException ex) =>
        ex.InnerException is SqliteException { SqliteErrorCode: 5 or 6 };

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
            builder.Property(s => s.PageLayoutMode).HasConversion<string>().HasMaxLength(32);
            // Same enum-as-string HasSentinel treatment as PageTransitionStyle above, even though
            // Unknown is both the CLR default and the desired default here - keeps every enum-as-
            // string column configured identically. HasDefaultValue matters here (unlike Series.
            // ContentType/ReadingMode above): this ALTER TABLE runs against a table with existing
            // rows, and the migration's own raw-SQL backfill (from the old IsComplete bool) needs a
            // valid starting value to overwrite.
            builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(32)
                .HasDefaultValue(SeriesStatus.Unknown)
                .HasSentinel(SeriesStatus.Unknown);

            builder.Property(s => s.ReadingStatus).HasConversion<string>().HasMaxLength(32)
                .HasDefaultValue(ReadingStatus.Unknown)
                .HasSentinel(ReadingStatus.Unknown);
            // Series.IsComplete is computed (Status == Completed), not mapped - EF would otherwise
            // try to persist a get-only property with no backing column.
            builder.Ignore(s => s.IsComplete);

            builder.HasIndex(s => s.Name);

            builder.HasMany(s => s.Issues)
                .WithOne(i => i.Series)
                .HasForeignKey(i => i.SeriesId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(s => s.TrackingLinks)
                .WithOne(t => t.Series)
                .HasForeignKey(t => t.SeriesId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(s => s.Titles)
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

            builder.HasMany(s => s.CollectionItems)
                .WithOne(ci => ci.Series)
                .HasForeignKey(ci => ci.SeriesId)
                .OnDelete(DeleteBehavior.Cascade);


            builder.HasMany(s => s.MetadataProposals)
                .WithOne(mp => mp.Series)
                .HasForeignKey(mp => mp.SeriesId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Issue>(builder =>
        {
            builder.HasKey(i => i.Id);
            builder.Property(i => i.ReadingModeOverride).HasConversion<string>().HasMaxLength(32);
            builder.Property(i => i.PageFitModeOverride).HasConversion<string>().HasMaxLength(32);
            builder.Property(i => i.PageLayoutModeOverride).HasConversion<string>().HasMaxLength(32);
            // Same treatment as Series.Status above - backfilled from the outgoing BlackAndWhite
            // bool column by this migration's raw SQL, needs a valid default to overwrite.
            builder.Property(i => i.ColorMode).HasConversion<string>().HasMaxLength(32)
                .HasDefaultValue(ColorMode.Unknown)
                .HasSentinel(ColorMode.Unknown);
            builder.HasIndex(i => i.SeriesId);
            builder.HasIndex(i => i.FilePath);

            builder.HasMany(i => i.CustomValues)
                .WithOne(cv => cv.Issue)
                .HasForeignKey(cv => cv.IssueId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(i => i.Bookmarks)
                .WithOne(bm => bm.Issue)
                .HasForeignKey(bm => bm.IssueId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(i => i.Tags)
                .WithOne(t => t.Issue)
                .HasForeignKey(t => t.IssueId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(i => i.MetadataProposals)
                .WithOne(mp => mp.Issue)
                .HasForeignKey(mp => mp.IssueId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IssueBookmark>(builder =>
        {
            builder.HasKey(bm => bm.Id);
            builder.HasIndex(bm => bm.IssueId);
        });

        modelBuilder.Entity<IssuePage>(builder =>
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.PageType).HasConversion<string>().HasMaxLength(32);
            builder.HasIndex(p => new { p.IssueId, p.PageNumber }).IsUnique();
        });

        // Brand-new table (like MetadataProposal below) - no existing rows to backfill via EF's own
        // HasDefaultValue/HasSentinel; the migration backfills existing Genre/Tags CSV data via raw
        // SQL before dropping those columns instead (docs/superpowers/specs/2026-08-23-weighted-
        // categorized-tags-design.md).
        modelBuilder.Entity<IssueTag>(builder =>
        {
            builder.HasKey(t => t.Id);
            builder.Property(t => t.Field).HasConversion<string>().HasMaxLength(32);
            builder.Property(t => t.Weight).HasConversion<string>().HasMaxLength(32);
            builder.Property(t => t.Value).IsRequired();
            builder.HasIndex(t => t.IssueId);
        });

        modelBuilder.Entity<SeriesTitle>(builder =>
        {
            builder.HasKey(t => t.Id);
            builder.Property(t => t.Value).IsRequired();
            builder.Property(t => t.Type).HasConversion<string>().HasMaxLength(32);
            builder.HasIndex(t => t.SeriesId);
        });

        modelBuilder.Entity<Collection>(builder =>
        {
            builder.HasKey(c => c.Id);
            builder.Property(c => c.Name).IsRequired();

            // Rule slots (docs/superpowers/specs/2026-08-30-smart-collections-design.md) - one-
            // directional FKs, no inverse nav on SmartList. SetNull rather than Cascade: deleting the
            // underlying SmartList reverts the collection to manual-only instead of deleting it too.
            builder.HasOne(c => c.IssueSmartList)
                .WithMany()
                .HasForeignKey(c => c.IssueSmartListId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasOne(c => c.SeriesSmartList)
                .WithMany()
                .HasForeignKey(c => c.SeriesSmartListId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasOne(c => c.NovelSmartList)
                .WithMany()
                .HasForeignKey(c => c.NovelSmartListId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<CollectionItem>(builder =>
        {
            builder.HasKey(ci => ci.Id);

            builder.HasOne(ci => ci.Collection)
                .WithMany(c => c.Items)
                .HasForeignKey(ci => ci.CollectionId)
                .OnDelete(DeleteBehavior.Cascade);

            // Series FK is configured on the Series entity above (HasMany(s => s.CollectionItems)).
            builder.HasOne(ci => ci.Issue)
                .WithMany(i => i.CollectionItems)
                .HasForeignKey(ci => ci.IssueId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(ci => ci.Book)
                .WithMany(b => b.CollectionItems)
                .HasForeignKey(ci => ci.BookId)
                .OnDelete(DeleteBehavior.Cascade);

            // Exactly one target set. Enforced in the DB (EnsureCreated honours it for test schemas
            // too) and guarded in CollectionService.AddItems so a bad call is a logged no-op.
            builder.ToTable(t => t.HasCheckConstraint(
                "CK_CollectionItem_OneTarget",
                "((\"SeriesId\" IS NOT NULL) + (\"IssueId\" IS NOT NULL) + (\"BookId\" IS NOT NULL)) = 1"));

            // Block duplicate membership per target kind.
            builder.HasIndex(ci => new { ci.CollectionId, ci.SeriesId })
                .IsUnique()
                .HasFilter("\"SeriesId\" IS NOT NULL");
            builder.HasIndex(ci => new { ci.CollectionId, ci.IssueId })
                .IsUnique()
                .HasFilter("\"IssueId\" IS NOT NULL");
            builder.HasIndex(ci => new { ci.CollectionId, ci.BookId })
                .IsUnique()
                .HasFilter("\"BookId\" IS NOT NULL");
        });

        // Brand-new table (like MediaRelation) - no existing rows to backfill. Same
        // both-sides-Cascade choice as MediaRelation's own config above (see its comment for why:
        // there's no interactive delete path in this codebase that should ever be blocked by a
        // relation existing).
        modelBuilder.Entity<CollectionRelation>(builder =>
        {
            builder.HasKey(r => r.Id);
            builder.Property(r => r.RelationType).HasConversion<string>().HasMaxLength(32);
            builder.HasIndex(r => r.SourceCollectionId);
            builder.HasIndex(r => r.TargetCollectionId);

            builder.HasOne(r => r.SourceCollection)
                .WithMany()
                .HasForeignKey(r => r.SourceCollectionId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(r => r.TargetCollection)
                .WithMany()
                .HasForeignKey(r => r.TargetCollectionId)
                .OnDelete(DeleteBehavior.Cascade);
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
            builder.Property(s => s.TargetKind).HasConversion<string>().HasMaxLength(16).HasDefaultValue(SmartListTargetKind.Issue);

            // Nested AND/OR groups (docs/superpowers/specs/2026-08-28-smartlist-engine-v2-design.md
            // §2) - one root group per list, cascade so deleting the list takes the whole tree with
            // it. FK lives on the group (SmartListConditionGroup.SmartListId, null for nested groups).
            builder.HasOne(s => s.RootGroup)
                .WithOne(g => g.SmartList)
                .HasForeignKey<SmartListConditionGroup>(g => g.SmartListId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SmartListConditionGroup>(builder =>
        {
            builder.HasKey(g => g.Id);
            builder.Property(g => g.Mode).HasConversion<string>().HasMaxLength(16);
            builder.HasIndex(g => g.SmartListId);
            builder.HasIndex(g => g.ParentGroupId);

            // Self-reference for nesting. Cascade so deleting a group removes its subtree; SQLite
            // honours ON DELETE CASCADE on a self-FK.
            builder.HasMany(g => g.ChildGroups)
                .WithOne(g => g.ParentGroup)
                .HasForeignKey(g => g.ParentGroupId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(g => g.Conditions)
                .WithOne(c => c.Group)
                .HasForeignKey(c => c.GroupId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SmartListCondition>(builder =>
        {
            builder.HasKey(c => c.Id);
            builder.Property(c => c.Field).HasConversion<string>().HasMaxLength(32);
            builder.Property(c => c.Operator).HasConversion<string>().HasMaxLength(32);
            // SearchMode is only set for AllProperties conditions; nullable, no backfill ambiguity,
            // so (like Issue.PageFitModeOverride etc.) just the conversion, no HasDefaultValue.
            builder.Property(c => c.SearchMode).HasConversion<string>().HasMaxLength(16);
            builder.HasIndex(c => c.GroupId);
        });

        modelBuilder.Entity<ReadingList>(builder =>
        {
            builder.HasKey(r => r.Id);
            builder.Property(r => r.Name).IsRequired();
            // Phase 4c (docs/superpowers/specs/2026-08-17-metadata-model-phase4c-reading-list-
            // overhaul-design.md) - this ALTER TABLE runs against a table with existing rows, so
            // (same reasoning as Series.Status/Issue.ColorMode above) needs a DB-level default to
            // backfill them with, not just the CLR default.
            builder.Property(r => r.Type).HasConversion<string>().HasMaxLength(32)
                .HasDefaultValue(ReadingListType.User)
                .HasSentinel(ReadingListType.Official);
            // Real bug caught before shipping: HasDefaultValueSql("CURRENT_TIMESTAMP") generates
            // an ALTER TABLE ADD COLUMN with a non-constant default, which SQLite rejects outright
            // ("Cannot add a column with non-constant default") - CURRENT_TIMESTAMP is only legal
            // as a column default at CREATE TABLE time, not when adding a column to an existing
            // table. Uses a literal constant default instead (legal for ALTER TABLE); the
            // migration's own raw SQL (see MetadataModelPhase4cReadingListOverhaul) immediately
            // overwrites every existing row with the real migration-run timestamp right after,
            // same two-step shape as Series.Status's own HasDefaultValue-constant + raw-SQL-
            // backfill precedent.
            builder.Property(r => r.CreatedAt).HasDefaultValue(DateTime.UnixEpoch);
            builder.Property(r => r.UpdatedAt).HasDefaultValue(DateTime.UnixEpoch);

            builder.HasMany(r => r.Items)
                .WithOne(i => i.ReadingList)
                .HasForeignKey(i => i.ReadingListId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(r => r.Tags)
                .WithOne(t => t.ReadingList)
                .HasForeignKey(t => t.ReadingListId)
                .OnDelete(DeleteBehavior.Cascade);

            // SetNull, not Cascade/Restrict - deleting the linked StoryEvent shouldn't delete a
            // curated reading list built from it or block the event's own deletion, just detach
            // the list back to a plain (still Type=Event-classified, if it was) list.
            builder.HasOne(r => r.StoryEvent)
                .WithMany()
                .HasForeignKey(r => r.StoryEventId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ReadingListItem>(builder =>
        {
            builder.HasKey(i => i.Id);
            builder.HasIndex(i => i.ReadingListId);
            builder.Property(i => i.Role).HasConversion<string>().HasMaxLength(32);

            // An Issue can appear in many reading lists (and more than once within the same
            // list, e.g. a crossover issue revisited later) - Restrict, not Cascade, so deleting
            // an Issue can't silently cascade-delete unrelated reading lists; callers must remove
            // the item explicitly first.
            builder.HasOne(i => i.Issue)
                .WithMany()
                .HasForeignKey(i => i.IssueId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Brand-new table (docs/superpowers/specs/2026-08-23-reading-list-tags-design.md) - no
        // existing rows to backfill, same shape as IssueTag's block above.
        modelBuilder.Entity<ReadingListTag>(builder =>
        {
            builder.HasKey(t => t.Id);
            builder.Property(t => t.Weight).HasConversion<string>().HasMaxLength(32);
            builder.Property(t => t.Value).IsRequired();
            builder.HasIndex(t => t.ReadingListId);
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

        // Brand-new table (like SeriesConflict above) - no existing rows to backfill, so its enum
        // columns need only the conversion, no HasDefaultValue/HasSentinel.
        modelBuilder.Entity<MetadataProposal>(builder =>
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Field).HasConversion<string>().HasMaxLength(32);
            builder.Property(p => p.Source).HasConversion<string>().HasMaxLength(32);
            builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(32);
            // Nullable - null is an unambiguous "not provider-sourced," same treatment as
            // AppSettings.LibraryActiveContentType above, no HasDefaultValue/HasSentinel needed.
            builder.Property(p => p.ProviderKey).HasConversion<string>().HasMaxLength(32);
            builder.HasIndex(p => p.Status);
            builder.HasIndex(p => p.SeriesId);
        });

        // Brand-new tables (like MetadataProposal above) - no existing rows to backfill.
        modelBuilder.Entity<MediaRelation>(builder =>
        {
            builder.HasKey(m => m.Id);
            builder.Property(m => m.RelationType).HasConversion<string>().HasMaxLength(32);
            builder.HasIndex(m => m.SourceSeriesId);
            builder.HasIndex(m => m.TargetSeriesId);
            builder.HasIndex(m => m.SourceCollectionId);
            builder.HasIndex(m => m.TargetCollectionId);

            // Exactly one of Series/Collection per side (docs/superpowers/specs/2026-08-30-media-
            // relation-collection-nodes-design.md) - mirrors CollectionItem's own exactly-one-
            // target CHECK pattern rather than a discriminator-enum shape.
            builder.ToTable(t => t.HasCheckConstraint(
                "CK_MediaRelation_OneSourceTarget",
                "((\"SourceSeriesId\" IS NOT NULL) + (\"SourceCollectionId\" IS NOT NULL)) = 1"));
            builder.ToTable(t => t.HasCheckConstraint(
                "CK_MediaRelation_OneTargetTarget",
                "((\"TargetSeriesId\" IS NOT NULL) + (\"TargetCollectionId\" IS NOT NULL)) = 1"));

            // Cascade, not Restrict - unlike ReadingListItem.Issue (a direct, interactive user
            // action), every existing Series-deletion path in this codebase
            // (SeriesReassignmentResolver, NeedsReviewViewModel.MergeSeriesInto, both from Phase
            // 2b) is automatic empty-series cleanup with no chance to pause and check for
            // relations first. Restrict would turn those into a real runtime FK-violation
            // regression the moment a relation exists; Cascade just quietly removes a relation
            // that's lost one of its two endpoints, which is the only sane outcome here. Same
            // reasoning extended to the new Collection FKs.
            builder.HasOne(m => m.SourceSeries)
                .WithMany()
                .HasForeignKey(m => m.SourceSeriesId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(m => m.TargetSeries)
                .WithMany()
                .HasForeignKey(m => m.TargetSeriesId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(m => m.SourceCollection)
                .WithMany()
                .HasForeignKey(m => m.SourceCollectionId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(m => m.TargetCollection)
                .WithMany()
                .HasForeignKey(m => m.TargetCollectionId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(m => m.Evidence)
                .WithOne(e => e.MediaRelation)
                .HasForeignKey(e => e.MediaRelationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RelationEvidence>(builder =>
        {
            builder.HasKey(e => e.Id);
            builder.Property(e => e.Provider).HasConversion<string>().HasMaxLength(32);
        });

        // Brand-new table (like MediaRelation above) - no existing rows to backfill.
        modelBuilder.Entity<Continuity>(builder =>
        {
            builder.HasKey(c => c.Id);
            builder.Property(c => c.Name).IsRequired();
            builder.HasIndex(c => c.Name);
        });

        // Explicit Continuity <-> Series join (docs/superpowers/specs/2026-08-28-continuity-editing-
        // design.md, Part C) - replaced the implicit skip-navigation join so a membership can carry
        // its own Note and SortOrder. Cascade from both endpoints, same reasoning as EventMembership:
        // the join row is meaningless once either side is gone. Unique (ContinuityId, SeriesId) keeps
        // AddSeriesToContinuity idempotent at the database level too.
        modelBuilder.Entity<ContinuityMembership>(builder =>
        {
            builder.HasKey(m => m.Id);
            builder.HasIndex(m => new { m.ContinuityId, m.SeriesId }).IsUnique();
            builder.HasIndex(m => m.SeriesId);

            builder.HasOne(m => m.Continuity)
                .WithMany(c => c.Memberships)
                .HasForeignKey(m => m.ContinuityId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(m => m.Series)
                .WithMany(s => s.ContinuityMemberships)
                .HasForeignKey(m => m.SeriesId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Brand-new tables (like Continuity above) - no existing rows to backfill.
        modelBuilder.Entity<StoryEvent>(builder =>
        {
            builder.HasKey(e => e.Id);
            builder.Property(e => e.Name).IsRequired();
            builder.HasIndex(e => e.Name);

            builder.HasMany(e => e.Members)
                .WithOne(m => m.StoryEvent)
                .HasForeignKey(m => m.StoryEventId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EventMembership>(builder =>
        {
            builder.HasKey(m => m.Id);
            builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(32);
            builder.HasIndex(m => m.StoryEventId);

            // Restrict, not Cascade - same reasoning as ReadingListItem.Issue below: deleting an
            // Issue that's a tracked event member should be a conscious action, not silent
            // membership loss.
            builder.HasOne(m => m.Issue)
                .WithMany()
                .HasForeignKey(m => m.IssueId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Brand-new tables (docs/superpowers/specs/2026-08-27-metadata-model-phase4d-event-relations-
        // design.md) - no existing rows to backfill. Both endpoint FKs use Cascade: an EventRelation
        // pointing at a deleted StoryEvent is in exactly the same situation EventMembership is
        // already in (which cascades on event deletion), so Cascade just removes a relation that's
        // lost one of its two endpoints - consistent with every other row hanging off a StoryEvent.
        modelBuilder.Entity<EventRelation>(builder =>
        {
            builder.HasKey(r => r.Id);
            builder.Property(r => r.RelationType).HasConversion<string>().HasMaxLength(32);
            builder.HasIndex(r => r.SourceEventId);
            builder.HasIndex(r => r.TargetEventId);

            builder.HasOne(r => r.SourceEvent)
                .WithMany()
                .HasForeignKey(r => r.SourceEventId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(r => r.TargetEvent)
                .WithMany()
                .HasForeignKey(r => r.TargetEventId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(r => r.Evidence)
                .WithOne(e => e.EventRelation)
                .HasForeignKey(e => e.EventRelationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EventRelationEvidence>(builder =>
        {
            builder.HasKey(e => e.Id);
            builder.Property(e => e.Provider).HasConversion<string>().HasMaxLength(32);
        });

        // Brand-new tables (docs/superpowers/specs/2026-08-27-metadata-model-phase4d-4g deferred
        // items). All cascade on their FKs - these are nag-suppression flags / a rebuildable
        // character index, not content, so they should never block an Issue/Event/Character delete.
        modelBuilder.Entity<EventSuggestionDismissal>(builder =>
        {
            builder.HasKey(d => d.Id);
            builder.HasIndex(d => new { d.StoryEventId, d.IssueId }).IsUnique();

            builder.HasOne(d => d.StoryEvent)
                .WithMany()
                .HasForeignKey(d => d.StoryEventId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(d => d.Issue)
                .WithMany()
                .HasForeignKey(d => d.IssueId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Character>(builder =>
        {
            builder.HasKey(c => c.Id);
            builder.Property(c => c.Name).IsRequired();
            builder.HasIndex(c => c.Name);
        });

        modelBuilder.Entity<CharacterAppearance>(builder =>
        {
            builder.HasKey(a => a.Id);
            builder.HasIndex(a => new { a.CharacterId, a.IssueId }).IsUnique();
            builder.HasIndex(a => a.IssueId);

            builder.HasOne(a => a.Character)
                .WithMany(c => c.Appearances)
                .HasForeignKey(a => a.CharacterId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(a => a.Issue)
                .WithMany()
                .HasForeignKey(a => a.IssueId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Brand-new tables (like StoryEvent above) - no existing rows to backfill. Cascade on the
        // Series FK for all three, same reasoning as MediaRelation/SeriesContinuity: every existing
        // Series-deletion path is automatic empty-series cleanup with no interactive moment to
        // check for external-data rows first, and these rows have no value once their Series is
        // gone.
        modelBuilder.Entity<ExternalMediaId>(builder =>
        {
            builder.HasKey(e => e.Id);
            builder.Property(e => e.Provider).HasConversion<string>().HasMaxLength(32);
            builder.Property(e => e.ExternalId).IsRequired();
            builder.HasIndex(e => e.SeriesId);

            builder.HasOne(e => e.Series)
                .WithMany()
                .HasForeignKey(e => e.SeriesId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExternalMetadataSnapshot>(builder =>
        {
            builder.HasKey(e => e.Id);
            builder.Property(e => e.Provider).HasConversion<string>().HasMaxLength(32);
            builder.Property(e => e.ExternalId).IsRequired();
            builder.Property(e => e.SchemaVersion).IsRequired();
            builder.HasIndex(e => e.SeriesId);

            builder.HasOne(e => e.Series)
                .WithMany()
                .HasForeignKey(e => e.SeriesId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExternalRating>(builder =>
        {
            builder.HasKey(e => e.Id);
            builder.Property(e => e.Provider).HasConversion<string>().HasMaxLength(32);
            builder.HasIndex(e => e.SeriesId);

            builder.HasOne(e => e.Series)
                .WithMany()
                .HasForeignKey(e => e.SeriesId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Singleton row (Id always 1) - one app-wide config record, not a generic key-value store.
        modelBuilder.Entity<AppSettings>(builder =>
        {
            builder.HasKey(a => a.Id);
            builder.Property(a => a.ActiveSkinKey).IsRequired().HasDefaultValue("default");
            builder.Property(a => a.OpenLastPage).HasDefaultValue(true);
            builder.Property(a => a.AutoNavigateComics).HasDefaultValue(true);
            builder.Property(a => a.BackupsToKeep).HasDefaultValue(5);
            builder.Property(a => a.AutoBackupEnabled).HasDefaultValue(true);
            builder.Property(a => a.AutoBackupMinIntervalHours).HasDefaultValue(4);
            builder.Property(a => a.ReverseRtlNavigation).HasDefaultValue(true);
            builder.Property(a => a.HighQualityPageDisplay).HasDefaultValue(true);
            builder.Property(a => a.ResetZoomOnPageChange).HasDefaultValue(false);
            builder.Property(a => a.MouseWheelSpeed).HasDefaultValue(2.0);
            // HasDefaultValue is required here (unlike other enum columns in this context) because,
            // unlike Series.ContentType/ReadingMode, this ALTER TABLE runs against a table with an
            // existing row (the AppSettings singleton) - without a DB-level default, SQLite has
            // nothing valid to backfill that row's new NOT NULL column with, and EF's own fallback
            // (empty string) isn't a parseable ImageFitMode, which would throw the next time
            // anyone's real, already-existing settings row got read.
            // .Metadata.SetSentinel makes explicit what EF already treats Original (0, the CLR
            // default) as implicitly: the "this looks unset, use the DB default instead" value on
            // insert. Silences EF's warning without changing behavior - AppSettings is a true
            // singleton, only ever inserted once via GetOrCreateAppSettings with a real C# default
            // (FitWidth, never Original), so the theoretical "someone explicitly chooses Original
            // as their very first insert" ambiguity this warns about never actually occurs in this
            // codebase's usage pattern. A nullable backing field would close the gap completely but
            // isn't worth the complexity for a case that can't happen here.
            builder.Property(a => a.DefaultPageFitMode).HasConversion<string>().HasMaxLength(32)
                .HasDefaultValue(ImageFitMode.FitWidth)
                .HasSentinel(ImageFitMode.Original);
            builder.Property(a => a.DefaultAutoRotate).HasDefaultValue(false);
            // Same enum-as-string HasSentinel treatment as DefaultPageFitMode/ImageBackgroundMode
            // above, even though None is both the CLR default and the desired default here - keeps
            // every enum-as-string AppSettings column configured identically rather than special-
            // casing the one case where they happen to coincide.
            builder.Property(a => a.PageTransitionStyle).HasConversion<string>().HasMaxLength(32)
                .HasDefaultValue(PageTransitionStyle.None)
                .HasSentinel(PageTransitionStyle.None);
            builder.Property(a => a.PageTransitionDurationMs).HasDefaultValue(250);
            // Same enum-as-string HasSentinel treatment as PageTransitionStyle/DefaultPageFitMode
            // above - Single is both the CLR default and the desired default here too.
            builder.Property(a => a.DefaultPageLayoutMode).HasConversion<string>().HasMaxLength(32)
                .HasDefaultValue(PageLayoutMode.Single)
                .HasSentinel(PageLayoutMode.Single);
            builder.Property(a => a.MagnifierZoom).HasDefaultValue(2.0);
            builder.Property(a => a.MagnifierOpacity).HasDefaultValue(1.0);
            builder.Property(a => a.MagnifierSizePixels).HasDefaultValue(200);
            builder.Property(a => a.DefaultBrightness).HasDefaultValue(0.0);
            builder.Property(a => a.DefaultContrast).HasDefaultValue(0.0);
            builder.Property(a => a.DefaultSaturation).HasDefaultValue(0.0);
            builder.Property(a => a.DefaultGamma).HasDefaultValue(0.0);
            // Same HasSentinel gotcha as DefaultPageFitMode above - ImageBackgroundMode.Auto (0) is
            // the CLR default, but the actual desired default is Color (1), so without the sentinel
            // EF can't distinguish "explicitly Auto" from "unset" on the singleton row's backfill.
            builder.Property(a => a.ImageBackgroundMode).HasConversion<string>().HasMaxLength(32)
                .HasDefaultValue(ImageBackgroundMode.Color)
                .HasSentinel(ImageBackgroundMode.Auto);
            builder.Property(a => a.BackgroundColor).IsRequired().HasDefaultValue("WhiteSmoke");
            builder.Property(a => a.PageMarginEnabled).HasDefaultValue(false);
            builder.Property(a => a.PageMarginPercentWidth).HasDefaultValue(0.05);
            builder.Property(a => a.ShowScrubberOverlay).HasDefaultValue(true);
            // Same reasoning as ShowScrubberOverlay/OpenLastPage above: desired default (true)
            // diverges from the CLR/SQLite implicit zero-value (false), so the existing singleton
            // row's backfill on this ALTER TABLE needs an explicit DB-level default or it lands on
            // false instead - the exact class of bug documented in the DefaultPageFitMode comment
            // above, just for a bool column instead of an enum-as-string one.
            builder.Property(a => a.CheckForUpdatesOnStartup).HasDefaultValue(true);

            // PosterGrid (0) is both the CLR default and the desired default here (Phase 4a
            // collapsed Compact/Comfortable/CoverOnly into it) - same coincide-and-still-set-it-for-
            // consistency case as LibraryIssueListGroupField.None / PageTransitionStyle.None above.
            builder.Property(a => a.LibraryViewMode).HasConversion<string>().HasMaxLength(32)
                .HasDefaultValue(LibraryViewMode.PosterGrid)
                .HasSentinel(LibraryViewMode.PosterGrid);
            // Same treatment - Number (0) is the CLR default, desired default is Added.
            builder.Property(a => a.LibraryIssueListSortField).HasConversion<string>().HasMaxLength(32)
                .HasDefaultValue(IssueListSortField.Added)
                .HasSentinel(IssueListSortField.Number);
            builder.Property(a => a.LibraryIssueListSortDirection).HasConversion<string>().HasMaxLength(32)
                .HasDefaultValue(SortDirection.Descending)
                .HasSentinel(SortDirection.Ascending);
            builder.Property(a => a.LibraryIssueListGroupField).HasConversion<string>().HasMaxLength(32)
                .HasDefaultValue(IssueListGroupField.None)
                .HasSentinel(IssueListGroupField.None);
            // Issue (0) is both the CLR default and the desired default here - sentinel added
            // anyway for consistency with every other enum-as-string column in this method.
            builder.Property(a => a.LibraryGranularity).HasConversion<string>().HasMaxLength(32)
                .HasDefaultValue(LibraryContentGranularity.Issue)
                .HasSentinel(LibraryContentGranularity.Issue);
            // Same enum-as-string HasSentinel treatment as LibraryIssueListGroupField above, even though
            // All is both the CLR default and the desired default here.
            builder.Property(a => a.LibrarySearchMode).HasConversion<string>().HasMaxLength(32)
                .HasDefaultValue(SearchMode.All)
                .HasSentinel(SearchMode.All);
            builder.Property(a => a.LibraryGridDensity).HasDefaultValue(1.0);
            builder.Property(a => a.LibraryShowTileTitles).HasDefaultValue(true);
            builder.Property(a => a.LibraryShowUnreadBadge).HasDefaultValue(true);
            builder.Property(a => a.LibraryShowPublisherBadge).HasDefaultValue(false);
            builder.Property(a => a.LibraryShowLanguageBadge).HasDefaultValue(false);
            builder.Property(a => a.LibraryUseLanguageIcon).HasDefaultValue(false);
            builder.Property(a => a.LibraryShowContinueReadingButton).HasDefaultValue(false);
            // Nullable - null is an unambiguous "no active filter"/"empty search", no backfill
            // ambiguity, so (unlike the non-nullable enum columns above) no HasDefaultValue/
            // HasSentinel needed. Same treatment as Issue.PageFitModeOverride etc.
            builder.Property(a => a.LibraryActiveContentType).HasConversion<string>().HasMaxLength(32);
            builder.Property(a => a.LibraryFilterUnreadOnly).HasDefaultValue(false);
            builder.Property(a => a.LibraryFilterMissingIssues).HasDefaultValue(false);
            builder.Property(a => a.LibraryFilterTrackedOnly).HasDefaultValue(false);

            // Books screen sort/group - same enum-as-string HasSentinel treatment as LibraryIssueListSortField
            // above (the singleton AppSettings row needs a parseable value backfilled into the new
            // NOT NULL columns).
            builder.Property(a => a.BooksSortField).HasConversion<string>().HasMaxLength(32)
                .HasDefaultValue(BooksSortField.Title)
                .HasSentinel(BooksSortField.Title);
            builder.Property(a => a.BooksSortDirection).HasConversion<string>().HasMaxLength(32)
                .HasDefaultValue(SortDirection.Ascending)
                .HasSentinel(SortDirection.Ascending);
            builder.Property(a => a.BooksGroupField).HasConversion<string>().HasMaxLength(32)
                .HasDefaultValue(BooksGroupField.None)
                .HasSentinel(BooksGroupField.None);

            // Same enum-as-string HasSentinel treatment as PageTransitionStyle/LibraryIssueListGroupField
            // above, even though Automatic is both the CLR default and the desired default here.
            builder.Property(a => a.MetadataResolutionPolicy).HasConversion<string>().HasMaxLength(32)
                .HasDefaultValue(MetadataResolutionPolicy.Automatic)
                .HasSentinel(MetadataResolutionPolicy.Automatic);
            // Same enum-as-string HasSentinel treatment as the columns above - Auto is both the CLR
            // default and the desired default. HasDefaultValue matters: this ALTER TABLE runs
            // against a table that already has the AppSettings singleton row, which needs a valid
            // parseable value backfilled into the new NOT NULL column.
            builder.Property(a => a.RenderingBackend).HasConversion<string>().HasMaxLength(32)
                .HasDefaultValue(RenderBackend.Auto)
                .HasSentinel(RenderBackend.Auto);
            builder.Property(a => a.PreferNativeOpenGl).HasDefaultValue(false);

            // Books reader ergonomics global defaults (docs/superpowers/specs/2026-09-01-books-
            // reader-ergonomics-and-annotations-design.md) - same enum-as-string HasSentinel
            // treatment as BooksSortField etc. above.
            builder.Property(a => a.BookReaderFontSize).HasDefaultValue(17.0);
            builder.Property(a => a.BookReaderFontFamily).HasConversion<string>().HasMaxLength(32)
                .HasDefaultValue(BookFontFamilyOption.Serif)
                .HasSentinel(BookFontFamilyOption.Serif);
            builder.Property(a => a.BookReaderLineSpacing).HasConversion<string>().HasMaxLength(32)
                .HasDefaultValue(BookLineSpacingOption.Normal)
                .HasSentinel(BookLineSpacingOption.Normal);
            builder.Property(a => a.BookReaderCharacterSpacing).HasDefaultValue(0.0);
            builder.Property(a => a.BookReaderWordSpacing).HasDefaultValue(0.0);
            builder.Property(a => a.BookReaderParagraphSpacing).HasDefaultValue(10.0);
            builder.Property(a => a.BookReaderPageMargin).HasDefaultValue(40.0);
            builder.Property(a => a.BookReaderTheme).HasConversion<string>().HasMaxLength(32)
                .HasDefaultValue(BookTheme.MatchAppSkin)
                .HasSentinel(BookTheme.MatchAppSkin);
            builder.Property(a => a.BookReaderAutoHideChrome).HasDefaultValue(true);
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

        modelBuilder.Entity<KeyBinding>(builder =>
        {
            builder.HasKey(k => k.Id);
            builder.Property(k => k.CommandId).IsRequired();
            builder.Property(k => k.Key).IsRequired();
            builder.HasIndex(k => k.CommandId).IsUnique();
        });

        // Saved Workspaces (docs/superpowers/specs/2026-09-03-library-saved-workspaces-design.md) -
        // a growable per-screen list, same plain-table shape as KeyBinding. Screen is enum-as-string
        // (this codebase's convention) with no HasSentinel: every row gets an explicit Screen on
        // insert (WorkspaceService.Create / seeding), never a backfill - same as Book.Format.
        modelBuilder.Entity<Workspace>(builder =>
        {
            builder.HasKey(w => w.Id);
            builder.Property(w => w.Screen).HasConversion<string>().HasMaxLength(16);
            builder.Property(w => w.Name).IsRequired();
            builder.Property(w => w.StateJson).IsRequired();
            builder.HasIndex(w => new { w.Screen, w.SortOrder });
        });

        // Novels (docs/superpowers/specs/2026-08-09-novels-epub-pdf-support-design.md §2) -
        // independent of the comic Series/Issue tables above, no FK crossing between the two.
        modelBuilder.Entity<BookSeries>(builder =>
        {
            builder.HasKey(s => s.Id);
            builder.Property(s => s.Name).IsRequired();
            builder.HasIndex(s => s.Name);

            builder.HasMany(s => s.Books)
                .WithOne(b => b.BookSeries)
                .HasForeignKey(b => b.BookSeriesId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Book>(builder =>
        {
            builder.HasKey(b => b.Id);
            builder.Property(b => b.Title).IsRequired();
            builder.Property(b => b.FilePath).IsRequired();
            builder.Property(b => b.Format).HasConversion<string>().HasMaxLength(32);
            builder.Property(b => b.Finished).HasDefaultValue(false);
            builder.Property(b => b.ChapterCount).HasDefaultValue(0);
            builder.HasIndex(b => b.FilePath);

            // Per-book reader-ergonomics overrides (docs/superpowers/specs/2026-09-01-books-reader-
            // ergonomics-and-annotations-design.md) - nullable, no HasDefaultValue/HasSentinel needed,
            // same treatment as Issue.PageFitModeOverride/LibraryActiveContentType above (null is an
            // unambiguous "no override", not a value needing a backfill default).
            builder.Property(b => b.FontFamilyOverride).HasConversion<string>().HasMaxLength(32);
            builder.Property(b => b.LineSpacingOverride).HasConversion<string>().HasMaxLength(32);
            builder.Property(b => b.ThemeOverride).HasConversion<string>().HasMaxLength(32);

            builder.HasMany(b => b.Bookmarks)
                .WithOne(bm => bm.Book)
                .HasForeignKey(bm => bm.BookId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(b => b.Highlights)
                .WithOne(h => h.Book)
                .HasForeignKey(h => h.BookId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(b => b.AnnotationImages)
                .WithOne(a => a.Book)
                .HasForeignKey(a => a.BookId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BookBookmark>(builder =>
        {
            builder.HasKey(bm => bm.Id);
            builder.HasIndex(bm => bm.BookId);
        });

        modelBuilder.Entity<BookHighlight>(builder =>
        {
            builder.HasKey(h => h.Id);
            builder.HasIndex(h => h.BookId);
            // BlockId (docs/superpowers/specs/2026-09-02-books-reflow-reader-webview-redesign-
            // design.md) - a BlockIdInjector-assigned "pb-p<n>" id, comfortably under 64 chars for
            // any realistic chapter length.
            builder.Property(h => h.BlockId).IsRequired().HasMaxLength(64);
            builder.Property(h => h.Color).HasConversion<string>().HasMaxLength(32)
                .HasDefaultValue(BookHighlightColor.Yellow)
                .HasSentinel(BookHighlightColor.Yellow);
        });

        modelBuilder.Entity<BookAnnotationImage>(builder =>
        {
            builder.HasKey(a => a.Id);
            builder.HasIndex(a => a.BookId);
        });

        modelBuilder.Entity<BookFolder>(builder =>
        {
            builder.HasKey(f => f.Id);
            builder.Property(f => f.Path).IsRequired();
            builder.HasIndex(f => f.Path).IsUnique();
        });

        // Activity Center history (docs/superpowers/specs/2026-09-03-activity-center-design.md) -
        // brand-new table, no existing rows to backfill, so its enum-as-string columns need only
        // the conversion (no HasDefaultValue/HasSentinel). Same plain growable-list shape as
        // KeyBinding/Workspace. Indexed on StartedUtc for the paged "newest first" History query.
        modelBuilder.Entity<ActivityRun>(builder =>
        {
            builder.HasKey(r => r.Id);
            builder.Property(r => r.Kind).HasConversion<string>().HasMaxLength(32);
            builder.Property(r => r.Trigger).HasConversion<string>().HasMaxLength(32);
            builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(32);
            builder.Property(r => r.Title).IsRequired();
            builder.Property(r => r.ResultLinkKind).HasMaxLength(32);
            builder.HasIndex(r => r.StartedUtc);
        });
    }

    /// <summary>
    /// Test-only redirect for <see cref="GetDefaultDatabasePath"/> - mutable so tests can point
    /// every <c>PaperbunkrDb.CreateContext()</c> call (App-side ViewModels have no injected
    /// context-factory seam, unlike <c>SkinService</c>/<c>CoverThumbnailService</c>) at a temp
    /// SQLite file instead of the real per-user database. Never set this outside a test's own
    /// constructor/teardown.
    /// </summary>
    /// <remarks>
    /// Seeded from the <c>PAPERBUNKR_DB_PATH</c> environment variable so the same redirect works
    /// for an out-of-process launch too (docs/superpowers/specs/2026-08-17-library-saved-list-
    /// layouts-design.md's UI automation harness starts the real compiled exe via
    /// <c>Process.Start</c>, which can't reach this in-process static any other way) - set the env
    /// var on the child process's environment before launch, not this property directly, for that
    /// case. In-process tests keep assigning the property explicitly, which still works exactly as
    /// before and simply overrides whatever the field initializer picked up.
    /// </remarks>
    public static string? DatabasePathOverride { get; set; } = Environment.GetEnvironmentVariable("PAPERBUNKR_DB_PATH");

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
