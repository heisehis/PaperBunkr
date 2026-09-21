using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// The safety property of remote library sharing (docs/superpowers/specs/2026-09-19-remote-library-
/// sharing-design.md §8): rows mirrored from another instance are invisible to every job that doesn't
/// explicitly opt in via <see cref="PaperbunkrDbContext.IncludeRemote"/>. A miss here silently
/// corrupts or deletes data, so this exercises real resolvers and the raw query surface.
/// </summary>
public class RemoteRowIsolationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_isolation_{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<PaperbunkrDbContext> _options;
    private int _sourceId, _localSeriesId, _remoteSeriesId, _localIssueId, _remoteIssueId;

    public RemoteRowIsolationTests()
    {
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = Create(includeRemote: true);
        context.Database.EnsureCreated();

        var source = new RemoteSource { InstanceId = "host-1", DisplayName = "Den PC", Host = "h", CertFingerprint = "x" };
        context.RemoteSources.Add(source);
        context.SaveChanges();
        _sourceId = source.Id;

        // Same name on purpose: a local scan must never resolve "Saga" to the remote series.
        var local = new Series { Name = "Saga" };
        var remote = new Series { Name = "Saga", RemoteSourceId = source.Id, RemoteSeriesId = 5 };
        context.Series.AddRange(local, remote);
        context.SaveChanges();
        (_localSeriesId, _remoteSeriesId) = (local.Id, remote.Id);

        var localIssue = new Issue { SeriesId = local.Id, Number = "1", FilePath = @"C:\a.cbz" };
        var remoteIssue = new Issue { SeriesId = remote.Id, Number = "1", RemoteSourceId = source.Id, RemoteIssueId = 50 };
        context.Issues.AddRange(localIssue, remoteIssue);
        context.SaveChanges();
        (_localIssueId, _remoteIssueId) = (localIssue.Id, remoteIssue.Id);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    private PaperbunkrDbContext Create(bool includeRemote = false) => new(_options) { IncludeRemote = includeRemote };

    [Fact]
    public void DefaultContext_SeesOnlyLocalRows()
    {
        using var context = Create();

        Assert.Equal(new[] { _localIssueId }, context.Issues.Select(i => i.Id).ToList());
        Assert.Equal(new[] { _localSeriesId }, context.Series.Select(s => s.Id).ToList());
        Assert.Equal(1, context.Issues.Count());
        Assert.False(context.Series.Any(s => s.Id == _remoteSeriesId));
    }

    [Fact]
    public void IncludeRemoteContext_SeesBoth()
    {
        using var context = Create(includeRemote: true);

        Assert.Equal(2, context.Issues.Count());
        Assert.Equal(2, context.Series.Count());
    }

    [Fact]
    public void NavigationCollections_DoNotLeakRemoteRows()
    {
        using var context = Create();

        var series = context.Series.Include(s => s.Issues).Single();

        Assert.Equal(new[] { _localIssueId }, series.Issues.Select(i => i.Id));
    }

    [Fact]
    public void NameBasedSeriesLookup_NeverResolvesToTheRemoteSeries()
    {
        using var context = Create();

        // The exact shape used by PaperbunkrApplication / LibraryScreenViewModel / MigrationViewModel /
        // PreferencesScreenViewModel / SeriesReassignmentResolver.
        var match = context.Series.FirstOrDefault(s => s.Name.ToLower() == "saga");

        Assert.Equal(_localSeriesId, match!.Id);
    }

    [Fact]
    public void Find_OfARemoteRow_ReflectsTheFilterOrIsHarmless()
    {
        // Verified empirically (2026-09-21): Find() honors the global filter, so the many Library/Detail commands that do
        // `context.X.Find(id)` then `if (row is not null)` are true no-ops for a remote id. The read-only-by-construction story for
        // those commands rests on this, so it is asserted strictly - a framework change that made Find() see remote rows would let
        // them edit and delete the mirror.
        using var context = Create();

        Assert.Null(context.Issues.Find(_remoteIssueId));
        Assert.Null(context.Series.Find(_remoteSeriesId));
    }

    [Fact]
    public void SeriesReassignment_ToANameThatOnlyExistsRemotely_CreatesALocalSeries_NotAttachToRemote()
    {
        int reassignedIssueId;
        using (var context = Create())
        {
            var other = new Series { Name = "Elsewhere" };
            context.Series.Add(other);
            context.SaveChanges();
            var issue = new Issue { SeriesId = other.Id, Number = "9", FilePath = @"C:\z.cbz" };
            context.Issues.Add(issue);
            context.SaveChanges();
            reassignedIssueId = issue.Id;

            // "Remote Only" exists solely as a mirrored series.
        }
        using (var seed = Create(includeRemote: true))
        {
            seed.Series.Add(new Series { Name = "Remote Only", RemoteSourceId = _sourceId, RemoteSeriesId = 99 });
            seed.SaveChanges();
        }

        using (var context = Create())
        {
            var proposal = new MetadataProposal { IssueId = reassignedIssueId, ProposedValue = "Remote Only" };
            SeriesReassignmentResolver.Apply(context, proposal);
        }

        using var check = Create(includeRemote: true);
        var moved = check.Issues.Single(i => i.Id == reassignedIssueId);
        var target = check.Series.Single(s => s.Id == moved.SeriesId);
        Assert.Null(target.RemoteSourceId);          // a fresh LOCAL series, not the mirrored one
        Assert.Equal("Remote Only", target.Name);
    }

    [Fact]
    public void RemovingTheSource_LeavesLocalDataUntouched_AndClearsTheMirror()
    {
        using (var context = Create(includeRemote: true))
        {
            context.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");
            context.RemoteSources.Remove(context.RemoteSources.Single(s => s.Id == _sourceId));
            context.SaveChanges();
        }

        using var check = Create(includeRemote: true);
        Assert.Equal(new[] { _localIssueId }, check.Issues.Select(i => i.Id).ToList());
        Assert.Equal(new[] { _localSeriesId }, check.Series.Select(s => s.Id).ToList());
    }

    [Fact]
    public void MirrorRows_NeverCarryALocalFilePath()
    {
        using var context = Create(includeRemote: true);

        Assert.All(context.Issues.Where(i => i.RemoteSourceId != null).ToList(), i => Assert.Null(i.FilePath));
    }

    [Fact]
    public void OptInSites_AreExplicit_AndMatchTheAllowlist()
    {
        // Every place that opts in to remote rows is a place a local-only job could start touching
        // them. Adding one must be a conscious edit here, not a drive-by.
        string root = FindSrcRoot();
        var optIns = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains("Tests"))
            .Where(f =>
            {
                string text = File.ReadAllText(f);
                return text.Contains("includeRemote: true") || text.Contains("IncludeRemote = true") || text.Contains("IgnoreQueryFilters");
            })
            .Select(f => Path.GetFileName(f))
            .OrderBy(n => n)
            .ToList();

        // Grows deliberately as views are made remote-aware (see the plan's Phase 2.4 / Phase 3).
        // PaperbunkrDbContext.cs (a doc comment showing the syntax) and PaperbunkrDb.cs (the factory parameter) are infrastructure, not opt-in sites.
        // Deliberate opt-ins, each for a view that shows remote books: the Library grid's load, the two Detail screens'
        // load/selection/mark-read contexts (their edit and scrape paths use default contexts and so can't see remote
        // rows - remote books are read-only by construction), and the whole reader (its writes are client-local state).
        var allowed = new[]
        {
            "PaperbunkrDb.cs", "PaperbunkrDbContext.cs",
            "LibraryScreenViewModel.cs", "DetailScreenViewModel.cs", "MangaDetailScreenViewModel.cs", "ReaderScreenViewModel.cs",
            // Not opt-in sites: these are the mirror machinery, which *refuses* a context that has not opted in (their exception
            // messages and docs say "IncludeRemote = true", which is what the scan matches).
            "RemoteMirrorSync.cs", "RemoteRelinkReconciler.cs",
            // Reading activity/"what to read next" deliberately include remote books; see RemoteReadingStatsTests for what stays local-only.
            "InsightsResolver.cs", "StatsResolver.cs",
            // Drops the stale covers of replaced remote books (looks their mirror rows up by remote id).
            "MainViewModel.cs",
        }.OrderBy(n => n).ToList();
        var unexpected = optIns.Except(allowed).ToList();

        Assert.True(unexpected.Count == 0, "New remote opt-in site(s) need review and an allowlist entry: " + string.Join(", ", unexpected));
    }

    private static string FindSrcRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Paperbunkr.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return Path.Combine(dir ?? throw new InvalidOperationException("Paperbunkr.sln not found above the test output"), "src");
    }
}
