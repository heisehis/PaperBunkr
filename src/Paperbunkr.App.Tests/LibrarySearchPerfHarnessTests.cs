using System.Diagnostics;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Xunit.Abstractions;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Stopwatch harness for docs/superpowers/specs/2026-09-19-library-search-perf-design.md. Opt-in
/// (set <c>PB_PERF=1</c>) so the normal suite never pays for seeding thousands of issues. Not a
/// pass/fail gate on absolute times except where the spec names one; it prints numbers that get
/// recorded in the plan's Results section before and after the change.
///
/// Not BenchmarkDotNet on purpose: <c>Paperbunkr.Benchmarks</c>' GlobalSetup is broken under BDN
/// in this repo (see the plan), and a typing sequence against the real view-model is what we need.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class LibrarySearchPerfHarnessTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public LibrarySearchPerfHarnessTests(ITestOutputHelper output)
    {
        _output = output;
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_search_perf_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;

        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch (IOException)
        {
        }
    }

    private static readonly string[] s_words =
    {
        "Batman", "Superman", "Spider", "Saga", "Hellboy", "Berserk", "Akira", "Warhammer", "Sandman",
        "Invincible", "Watchmen", "Preacher", "Locke", "Monstress", "Paper", "Girls", "Descender", "Bone",
        "Fables", "Transmet", "Ultimate", "Absolute", "Dark", "Knight", "Noir", "Origins",
    };

    private static readonly string[] s_writers = { "Miller", "Moore", "Gaiman", "Vaughan", "Abnett", "Morrison", "Ennis" };

    private static void Seed(int seriesCount, int issueCount)
    {
        using var context = PaperbunkrDb.CreateContext();
        var random = new Random(1234);

        var series = new List<Series>();
        for (int s = 0; s < seriesCount; s++)
        {
            series.Add(new Series
            {
                Name = $"{s_words[random.Next(s_words.Length)]} {s_words[random.Next(s_words.Length)]} {s}",
                ContentType = ContentType.Comic,
                Publisher = s % 3 == 0 ? "Marvel" : "Image",
            });
        }

        context.Series.AddRange(series);
        context.SaveChanges();

        var issues = new List<Issue>(issueCount);
        for (int i = 0; i < issueCount; i++)
        {
            var owner = series[i % seriesCount];
            issues.Add(new Issue
            {
                SeriesId = owner.Id,
                Number = ((i / seriesCount) + 1).ToString(),
                Writer = s_writers[random.Next(s_writers.Length)],
                Publisher = owner.Publisher,
                Summary = $"{s_words[random.Next(s_words.Length)]} meets {s_words[random.Next(s_words.Length)]} in issue {i}.",
                FilePath = $@"C:\Comics\{owner.Name}\{owner.Name} {(i / seriesCount) + 1:000}.cbz",
                FileSize = 50_000_000 + i,
                AddedTime = DateTime.UtcNow.AddMinutes(-i),
            });
        }

        context.Issues.AddRange(issues);
        context.SaveChanges();
    }

    [Theory]
    [InlineData(500, 3000)]
    [InlineData(1500, 10000)]
    public void TypingSequence_TimesEveryKeystroke(int seriesCount, int issueCount)
    {
        if (Environment.GetEnvironmentVariable("PB_PERF") != "1")
        {
            return;
        }

        Seed(seriesCount, issueCount);

        var vm = new LibraryScreenViewModel(
            _ => { }, _ => { }, (_, _, _) => { }, _ => { }, _ => { }, _ => { }, (_, _) => { }, _ => { }, () => { }, _ => { }, _ => { });

        var keystrokes = new[] { "b", "ba", "bat", "batm", "batma", "batman", "batman ", "batman b", string.Empty };
        var samples = new List<double>();

        // Warm-up pass so JIT/first-touch cost is not counted.
        vm.SearchQuery = "warm";
        vm.SearchQuery = string.Empty;

        var sw = new Stopwatch();
        foreach (string query in keystrokes)
        {
            sw.Restart();
            vm.SearchQuery = query;
            sw.Stop();
            samples.Add(sw.Elapsed.TotalMilliseconds);
            _output.WriteLine($"[{seriesCount}/{issueCount}] query '{query}': {sw.Elapsed.TotalMilliseconds:F1} ms, rows={vm.IssueList.FlatRows.Count}, cards={vm.FlatCovers.Count}");
        }

        samples.Sort();
        _output.WriteLine($"[{seriesCount}/{issueCount}] median {samples[samples.Count / 2]:F1} ms, max {samples[^1]:F1} ms");
    }

    /// <summary>
    /// What production actually pays on the UI thread: the keystroke handler alone (the debounced job and the
    /// settings write are only scheduled), then the worker-side compute, then the UI-thread swap. Uses the
    /// manual scheduler so each half can be timed separately.
    /// </summary>
    [Theory]
    [InlineData(500, 3000)]
    [InlineData(1500, 10000)]
    public void TypingSequence_UiThreadBreakdown(int seriesCount, int issueCount)
    {
        if (Environment.GetEnvironmentVariable("PB_PERF") != "1")
        {
            return;
        }

        Seed(seriesCount, issueCount);
        var vm = new LibraryScreenViewModel(
            _ => { }, _ => { }, (_, _, _) => { }, _ => { }, _ => { }, _ => { }, (_, _) => { }, _ => { }, () => { }, _ => { }, _ => { });
        var scheduler = new ManualLibraryViewScheduler();
        vm.ViewScheduler = scheduler;

        vm.SearchQuery = "warm";
        scheduler.RunPending();
        vm.SearchQuery = string.Empty;
        scheduler.RunPending();

        var handler = new List<double>();
        var compute = new List<double>();
        var apply = new List<double>();
        var sw = new Stopwatch();
        foreach (string query in new[] { "b", "ba", "bat", "batm", "batma", "batman", "batman ", "batman b", string.Empty })
        {
            sw.Restart();
            vm.SearchQuery = query;
            sw.Stop();
            handler.Add(sw.Elapsed.TotalMilliseconds);

            scheduler.RunPending();
            compute.Add(scheduler.LastComputeMs);
            apply.Add(scheduler.LastApplyMs);
            _output.WriteLine($"[{seriesCount}/{issueCount}] '{query}': UI-thread keystroke handler {handler[^1]:F2} ms | worker compute {scheduler.LastComputeMs:F2} ms | UI swap {scheduler.LastApplyMs:F2} ms");
        }

        _output.WriteLine($"[{seriesCount}/{issueCount}] max handler {handler.Max():F2} ms, max swap {apply.Max():F2} ms, max compute (off UI thread) {compute.Max():F2} ms");
    }

    /// <summary>The design doc's gate: one suggestion ranking must stay sub-millisecond (p95) at 10k issues.</summary>
    [Fact]
    public void SuggestionRanking_P95_IsUnderOneMillisecond_AtALargePool()
    {
        if (Environment.GetEnvironmentVariable("PB_PERF") != "1")
        {
            return;
        }

        var random = new Random(7);
        const string alphabet = "abcdefghijklmnopqrstuvwxyz ";
        string Word() => new(Enumerable.Range(0, random.Next(4, 20)).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
        var pool = Enumerable.Range(0, 20_000).Select(_ => Word().Trim()).Where(w => w.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var list = new Paperbunkr.App.Services.LibrarySearch.SuggestionCandidateList(pool);

        for (int i = 0; i < 200; i++)
        {
            list.Rank("zq", 6); // warm-up
        }

        var samples = new List<double>();
        var sw = new Stopwatch();
        var queries = new[] { "b", "ba", "bat", "zq", "xyzzy", "e", "ing", "th", "qq", "a b" };
        for (int i = 0; i < 2000; i++)
        {
            string query = queries[i % queries.Length];
            sw.Restart();
            list.Rank(query, 6);
            sw.Stop();
            samples.Add(sw.Elapsed.TotalMilliseconds);
        }

        samples.Sort();
        double p95 = samples[(int)(samples.Count * 0.95)];
        _output.WriteLine($"pool={pool.Count}: p50 {samples[samples.Count / 2]:F3} ms, p95 {p95:F3} ms, max {samples[^1]:F3} ms");
        Assert.True(p95 < 1.0, $"p95 {p95:F3} ms exceeds the 1 ms gate");
    }
}
