using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.EventMap;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using static Paperbunkr.App.Tests.EventMap.EventMapFixtures;

namespace Paperbunkr.App.Tests.EventMap;

/// <summary>Continuity map M5 (docs/superpowers/specs/2026-11-11-continuity-map-structure-links-design.md).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public sealed class EventMapStructureTests
{
    private const int EventId = 1;
    private const int Wolverine = 7, XMen = 8;

    // ===================== Pure =====================

    [Theory]
    [InlineData("Wolverine, Cyclops", "Wolverine")]
    [InlineData(" X-Men; Avengers", "X-Men")]
    [InlineData("  ", null)]
    [InlineData(null, null)]
    public void FaceNameIsTheFirstSegment(string? text, string? expected) => Assert.Equal(expected, EventMapStructureLoader.FaceName(text));

    // ===================== View model =====================

    private static (EventMapViewModel Vm, EventMapRow[] Rows) Load(int count = 4, Func<int, MapIssueStructure>? perIssue = null,
        IReadOnlyList<MapBracket>? brackets = null, bool indexReady = true)
    {
        var rows = Enumerable.Range(0, count).Select(i => Row(i % 2 == 0 ? SeriesA : SeriesB, $"{i + 1}", i + 1)).ToArray();
        perIssue ??= i => new MapIssueStructure(i % 2 == 0 ? new[] { Wolverine } : Array.Empty<int>(), i < 2 ? new[] { XMen } : Array.Empty<int>(),
            new MapCreator(100 + (i % 3), $"Writer {i % 3}"), null, null, null);
        var issues = rows.Select((r, i) => (r.IssueId, perIssue(i))).ToDictionary(x => x.IssueId, x => x.Item2);
        var entities = new List<MapEntity>
        {
            new(ComicMetadataEntityKind.Character, Wolverine, "Wolverine", issues.Values.Count(s => s.Characters.Contains(Wolverine))),
            new(ComicMetadataEntityKind.Team, XMen, "X-Men", issues.Values.Count(s => s.Teams.Contains(XMen))),
        };
        var source = Source("No match", rows) with { Structure = new EventMapStructure(issues, entities, brackets ?? Array.Empty<MapBracket>(), indexReady) };
        var vm = new EventMapViewModel(load: _ => source, activity: new ActivityService(dispatch: a => a(), recordRun: _ => { }),
            runInBackground: work => Task.FromResult(work()));
        vm.LoadAsync(EventId).GetAwaiter().GetResult();
        return (vm, rows);
    }

    [Fact]
    public void FeaturingDimsOthersAndHideOthersRemovesThem()
    {
        var (vm, _) = Load();
        Assert.Contains("Wolverine · 2", vm.FeaturingOptions);
        Assert.Null(vm.FeatureSet);

        vm.FeaturingText = "Wolverine · 2";
        Assert.True(vm.IsFeaturing);
        Assert.Equal("featuring Wolverine · 2 issues", vm.FeaturingStatus);
        Assert.Equal(new[] { 0, 2 }, vm.FeatureSet!.OrderBy(i => i));

        vm.FeaturingHideOthers = true;
        Assert.Equal(2, vm.Cards.Count);
        Assert.Equal(new[] { "1", "3" }, vm.Cards.Select(c => c.Number));

        vm.ClearFeaturingCommand.Execute(null);
        Assert.Equal(4, vm.Cards.Count);
        Assert.Null(vm.FeatureSet);
    }

    [Fact]
    public void ColourByWriterGivesTheTopCreatorsColoursAndOthersTheFaintBar()
    {
        // Eight issues by eight different writers: six get colours, two share "others".
        var (vm, _) = Load(8, i => new MapIssueStructure(Array.Empty<int>(), Array.Empty<int>(), new MapCreator(200 + i, $"W{i}"), null, null, null));
        vm.ColourByText = "Writer";
        Assert.Equal(EventMapColourBy.Writer, vm.ColourBy);
        Assert.Equal(7, vm.Legend.Count);
        Assert.True(vm.Legend[^1].IsOthers);
        Assert.Equal(2, vm.BarColors!.Count(c => c == EventMapViewModel.BarFaint));
        Assert.Equal(6, vm.BarColors!.Where(c => c >= 0).Distinct().Count());

        vm.HighlightCreatorCommand.Execute(vm.Legend[0]);
        Assert.Single(vm.FeatureSet!);
        vm.ColourByText = "Read state";
        Assert.Empty(vm.Legend);
        Assert.All(vm.BarColors!, c => Assert.Equal(EventMapViewModel.BarUnread, c));
        Assert.Null(vm.FeatureSet);                    // changing Colour by clears the highlight

        vm.ColourByText = "Lane";
        Assert.Null(vm.BarColors);
    }

    [Fact]
    public void BracketsSplitIntoContiguousStretchesWithTheLabelOnTheFirst()
    {
        var rows = Enumerable.Range(0, 4).Select(i => Row(SeriesA, $"{i + 1}", i + 1)).ToArray();
        // An arc over issues 1, 2 and 4 of one lane: two stretches.
        var arc = new MapBracket(SeriesA, MapBracketKind.Arc, "", null, "Dark Phoenix", "Dark Phoenix · #1–#4", new[] { rows[0].IssueId, rows[1].IssueId, rows[3].IssueId });
        var run = new MapBracket(SeriesA, MapBracketKind.Run, "Writer", 100, "Claremont · #1–#4", "Claremont", rows.Select(r => r.IssueId).ToList());
        var source = Source("No match", rows) with
        {
            Structure = new EventMapStructure(rows.ToDictionary(r => r.IssueId, _ => new MapIssueStructure(Array.Empty<int>(), Array.Empty<int>(),
                new MapCreator(100, "Claremont"), null, null, null)), Array.Empty<MapEntity>(), new[] { arc, run }, true),
        };
        var vm = new EventMapViewModel(load: _ => source, activity: new ActivityService(dispatch: a => a(), recordRun: _ => { }),
            runInBackground: work => Task.FromResult(work()));
        vm.LoadAsync(EventId).GetAwaiter().GetResult();

        Assert.False(vm.ShowArcs);                     // event maps start with arcs off
        Assert.Empty(vm.Brackets);
        vm.ToggleArcsCommand.Execute(null);
        var arcs = vm.Brackets.Where(b => b.Kind == MapBracketKind.Arc).ToList();
        Assert.Equal(2, arcs.Count);
        Assert.Equal("Dark Phoenix", arcs[0].Label);
        Assert.Null(arcs[1].Label);
        Assert.Equal((0, 1), (arcs[0].FirstColumn, arcs[0].LastColumn));
        Assert.Equal((3, 3), (arcs[1].FirstColumn, arcs[1].LastColumn));
        Assert.DoesNotContain(vm.Brackets, b => b.Kind == MapBracketKind.Run);   // runs only with Writer / Artist colouring

        vm.ColourByText = "Writer";
        var runSpan = Assert.Single(vm.Brackets, b => b.Kind == MapBracketKind.Run);
        Assert.Equal((0, 3), (runSpan.FirstColumn, runSpan.LastColumn));
        Assert.Equal(vm.Legend[0].ColorIndex, runSpan.ColorIndex);
        vm.ToggleCreatorHighlight(100);
        Assert.Equal(4, vm.FeatureSet!.Count);
    }

    [Fact]
    public void FeaturingFromAnEntityPageWaitsForTheMapToLoad()
    {
        var rows = new[] { Row(SeriesA, "1", 1), Row(SeriesB, "1", 2) };
        var source = Source("No match", rows) with
        {
            Structure = new EventMapStructure(new Dictionary<int, MapIssueStructure>
            {
                [rows[0].IssueId] = new(new[] { Wolverine }, Array.Empty<int>(), null, null, null, null),
                [rows[1].IssueId] = new(Array.Empty<int>(), Array.Empty<int>(), null, null, null, null),
            }, new[] { new MapEntity(ComicMetadataEntityKind.Character, Wolverine, "Wolverine", 1) }, Array.Empty<MapBracket>(), true),
        };
        var vm = new EventMapViewModel(load: _ => source, activity: new ActivityService(dispatch: a => a(), recordRun: _ => { }),
            runInBackground: work => Task.FromResult(work()));
        vm.SetFeaturing(ComicMetadataEntityKind.Character, Wolverine);   // before the map has data
        Assert.False(vm.IsFeaturing);
        vm.LoadAsync(EventId).GetAwaiter().GetResult();
        Assert.True(vm.IsFeaturing);
        Assert.Equal(new[] { 0 }, vm.FeatureSet!);
    }

    [Fact]
    public void FacesComeFromTheStructureAndAnUnbuiltIndexSaysSo()
    {
        var (vm, _) = Load(2, i => new MapIssueStructure(Array.Empty<int>(), Array.Empty<int>(), null, null, "Wolverine", i == 0 ? @"C:\faces\wolverine.jpg" : null),
            indexReady: false);
        Assert.Equal(@"C:\faces\wolverine.jpg", vm.Cards[0].FacePath);
        Assert.Null(vm.Cards[1].FacePath);
        Assert.True(vm.IsIndexBuilding);
    }

    // ===================== Loader (throwaway database) =====================

    [Fact]
    public void LoaderReadsAppearancesFirstCreditsAndRuns()
    {
        string path = Path.Combine(Path.GetTempPath(), $"paperbunkr_map_structure_{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={path}").Options;
            using var context = new PaperbunkrDbContext(options);
            context.Database.EnsureCreated();
            var series = new Series { Name = "Uncanny X-Men", ContentType = ContentType.Comic };
            context.Series.Add(series);
            var issues = Enumerable.Range(1, 7).Select(n => new Issue { Series = series, Number = n.ToString(), FilePath = $"x{n}.cbz", Title = n <= 2 ? $"Days of Future Past, Part {n}" : $"Story {n}" }).ToList();
            context.Issues.AddRange(issues);
            var logan = new Character { Name = "Wolverine" };
            var team = new Team { Name = "X-Men" };
            var claremont = new Creator { Name = "Chris Claremont" };
            var byrne = new Creator { Name = "John Byrne" };
            context.AddRange(logan, team, claremont, byrne);
            context.SaveChanges();
            context.CharacterAppearances.Add(new CharacterAppearance { CharacterId = logan.Id, IssueId = issues[0].Id });
            context.TeamAppearances.Add(new TeamAppearance { TeamId = team.Id, IssueId = issues[1].Id });
            foreach (var issue in issues)
            {
                context.CreatorCredits.Add(new CreatorCredit { CreatorId = claremont.Id, IssueId = issue.Id, Role = "Writer" });
            }

            context.CreatorCredits.Add(new CreatorCredit { CreatorId = byrne.Id, IssueId = issues[0].Id, Role = "Penciller" });
            context.SaveChanges();

            var structure = EventMapStructureLoader.Load(context, issues.Select(i => i.Id).ToList());
            Assert.Equal(new[] { logan.Id }, structure.Issues[issues[0].Id].Characters);
            Assert.Equal(new[] { team.Id }, structure.Issues[issues[1].Id].Teams);
            Assert.Equal("Chris Claremont", structure.Issues[issues[3].Id].Writer!.Name);
            Assert.Equal("John Byrne", structure.Issues[issues[0].Id].Artist!.Name);
            Assert.Contains(structure.Entities, e => e.Kind == ComicMetadataEntityKind.Character && e.Name == "Wolverine" && e.Count == 1);
            var run = Assert.Single(structure.Brackets, b => b.Kind == MapBracketKind.Run);
            Assert.Equal(7, run.IssueIds.Count);
            Assert.Equal(claremont.Id, run.CreatorId);
            Assert.False(structure.IndexReady);        // a fresh database hasn't run the index rebuild
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
