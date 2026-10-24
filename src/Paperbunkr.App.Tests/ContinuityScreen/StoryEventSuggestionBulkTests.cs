using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Paperbunkr.App.Services;
using Paperbunkr.App.Tests.Theming;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests.ContinuityScreen;

[Collection(nameof(AvaloniaTestCollection))]
public class StoryEventSuggestionBulkTests : ContinuityScreenTestBase
{
    private static void SeedCandidates(params string[] arcs)
    {
        using var context = PaperbunkrDb.CreateContext();
        foreach (string arc in arcs)
        {
            var series = new Series { Name = arc + " series" };
            series.Issues.Add(new Issue { Number = "1", StoryArc = arc, Publisher = "Marvel" });
            series.Issues.Add(new Issue { Number = "2", StoryArc = arc, Publisher = "Marvel" });
            context.Series.Add(series);
        }
        context.SaveChanges();
    }

    private static void PumpUntil(Func<bool> finished)
    {
        var timeout = Stopwatch.StartNew();
        while (!finished() && timeout.Elapsed < TimeSpan.FromSeconds(20))
        {
            Drain();
            Thread.Sleep(1);
        }
        Assert.True(finished(), "Batch did not finish within 20 seconds.");
        Drain();
    }

    private static void Complete(Task batch)
    {
        PumpUntil(() => batch.IsCompleted);
        batch.GetAwaiter().GetResult();
    }

    [Fact]
    public void SelectionControls_UpdateCountAndCommandAvailability()
    {
        SeedCandidates("Civil War", "Secret Invasion");
        var screen = CreateScreen();
        screen.RefreshStoryEventCandidates();
        var vm = screen.Suggestions;
        Assert.Equal(2, vm.NewStoryEventCandidates.Count);
        Assert.False(vm.AcceptSelectedStoryEventsCommand.CanExecute(null));
        vm.SelectAllStoryEventsCommand.Execute(null);
        Assert.Equal(2, vm.SelectedStoryEventCount);
        Assert.Equal("2 selected", vm.StoryEventSelectionLabel);
        Assert.True(vm.AcceptSelectedStoryEventsCommand.CanExecute(null));
        Assert.True(vm.DismissSelectedStoryEventsCommand.CanExecute(null));
        vm.ClearStoryEventSelectionCommand.Execute(null);
        Assert.Equal(0, vm.SelectedStoryEventCount);
        Assert.False(vm.AcceptSelectedStoryEventsCommand.CanExecute(null));
    }

    [Fact]
    public void AcceptSelected_ContinuesPastFailure_RetainsFailureForRetry_AndDoesNotNavigate()
    {
        SeedCandidates("Civil War", "Secret Invasion", "Infinity");
        var notices = new List<string>();
        var screen = CreateScreen(notify: (title, _) => notices.Add(title));
        screen.RefreshStoryEventCandidates();
        var vm = screen.Suggestions;
        var first = vm.NewStoryEventCandidates.Single(row => row.ArcName == "Civil War");
        var second = vm.NewStoryEventCandidates.Single(row => row.ArcName == "Secret Invasion");
        first.IsSelected = second.IsSelected = true;
        var activeBefore = screen.ActiveEventId;
        vm.VerifyStoryEventCandidate = (_, candidate) => candidate.ArcName == "Civil War"
            ? throw new InvalidOperationException("Provider temporarily unavailable")
            : Task.FromResult(candidate);

        Complete(vm.AcceptSelectedStoryEventsCommand.ExecuteAsync(null));

        using (var context = PaperbunkrDb.CreateContext())
        {
            var created = Assert.Single(context.StoryEvents);
            Assert.Equal("Secret Invasion", created.Name);
            Assert.Equal(2, context.EventMemberships.Count(m => m.StoryEventId == created.Id));
        }
        Assert.Equal(activeBefore, screen.ActiveEventId);
        Assert.Contains(screen.Sidebar.Events, storyEvent => storyEvent.Name == "Secret Invasion");
        Assert.Equal(2, vm.NewStoryEventCandidates.Count);
        Assert.True(first.IsSelected);
        Assert.Equal("Provider temporarily unavailable", first.BatchError);
        Assert.False(vm.NewStoryEventCandidates.Single(row => row.ArcName == "Infinity").IsSelected);
        Assert.Equal(2, vm.StoryEventBatchCompleted);
        Assert.Equal(2, vm.StoryEventBatchTotal);
        Assert.Contains("1 accepted, 1 failed", vm.StoryEventBatchStatus);
        Assert.False(vm.IsStoryEventBatchRunning);
        Assert.True(vm.AcceptSelectedStoryEventsCommand.CanExecute(null));
        Assert.DoesNotContain("Story event created", notices);

        vm.VerifyStoryEventCandidate = (_, candidate) => Task.FromResult(candidate);
        Complete(vm.AcceptSelectedStoryEventsCommand.ExecuteAsync(null));
        Assert.Single(vm.NewStoryEventCandidates);
        Assert.Equal("Infinity", vm.NewStoryEventCandidates.Single().ArcName);
        Assert.Contains("1 accepted, 0 failed", vm.StoryEventBatchStatus);
        Assert.Equal(activeBefore, screen.ActiveEventId);
    }

    [Fact]
    public void DismissSelected_IsRemembered_AndLeavesUnselectedCandidates()
    {
        SeedCandidates("Civil War", "Secret Invasion");
        var screen = CreateScreen();
        screen.RefreshStoryEventCandidates();
        var vm = screen.Suggestions;
        vm.NewStoryEventCandidates.Single(row => row.ArcName == "Civil War").IsSelected = true;
        var activeBefore = screen.ActiveEventId;

        Complete(vm.DismissSelectedStoryEventsCommand.ExecuteAsync(null));

        Assert.Equal("Secret Invasion", Assert.Single(vm.NewStoryEventCandidates).ArcName);
        screen.RefreshStoryEventCandidates();
        Assert.Equal("Secret Invasion", Assert.Single(vm.NewStoryEventCandidates).ArcName);
        Assert.Contains("1 dismissed, 0 failed", vm.StoryEventBatchStatus);
        Assert.Equal(activeBefore, screen.ActiveEventId);
        using var context = PaperbunkrDb.CreateContext();
        Assert.Empty(context.StoryEvents);
    }

    [Fact]
    public void RunningBatch_BlocksRowActionsAndRefresh_AndPublishesProgress()
    {
        SeedCandidates("Civil War", "Secret Invasion");
        var screen = CreateScreen();
        screen.RefreshStoryEventCandidates();
        var vm = screen.Suggestions;
        var rows = vm.NewStoryEventCandidates.ToList();
        vm.SelectAllStoryEventsCommand.Execute(null);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.VerifyStoryEventCandidate = async (_, candidate) =>
        {
            await release.Task;
            return candidate;
        };

        var batch = vm.AcceptSelectedStoryEventsCommand.ExecuteAsync(null);
        try
        {
            Assert.True(vm.IsStoryEventBatchRunning);
            Assert.False(vm.CanChangeStoryEvents);
            Assert.False(vm.DismissSelectedStoryEventsCommand.CanExecute(null));
            Assert.All(rows, row => Assert.False(row.AcceptCommand.CanExecute(null)));
            Assert.All(rows, row => Assert.False(row.DismissCommand.CanExecute(null)));
            vm.RefreshStoryEventCandidates();
            Assert.Equal(rows, vm.NewStoryEventCandidates);
            Assert.Contains("1 of 2", vm.StoryEventBatchStatus);
        }
        finally
        {
            release.TrySetResult();
            Complete(batch);
        }
        Assert.Empty(vm.NewStoryEventCandidates);
        Assert.Equal(2, vm.StoryEventBatchCompleted);
        Assert.False(vm.IsStoryEventBatchRunning);
        Assert.True(vm.CanChangeStoryEvents);
    }

    [Theory]
    [InlineData("default", 640)]
    [InlineData("daylight", 1000)]
    [InlineData("matrix", 640)]
    public void BulkToolbar_LoadsAndFits_WithAccessibleCheckboxes(string theme, int width)
    {
        SeedCandidates("Civil War", "Secret Invasion");
        var screen = CreateScreen();
        screen.RefreshStoryEventCandidates();
        FoundationStyleTests.WithFoundation(theme, () =>
        {
            var view = new SuggestionsChecksView { DataContext = screen.Suggestions };
            var window = new Window
            {
                Content = new ScrollViewer { Content = view }, Width = width, Height = 700,
                Background = (Avalonia.Media.IBrush)Application.Current!.FindResource("PbSurface0Brush")!,
            };
            window.Show();
            try
            {
                window.UpdateLayout();
                Drain();
                window.UpdateLayout();
                var checks = view.GetVisualDescendants().OfType<CheckBox>()
                    .Where(box => AutomationProperties.GetName(box) is "Select Civil War" or "Select Secret Invasion").ToList();
                Assert.Equal(2, checks.Count);
                checks[0].IsChecked = true;
                Assert.Equal(1, screen.Suggestions.SelectedStoryEventCount);
                var accept = view.GetVisualDescendants().OfType<Button>().First(button => button.Content as string == "Accept selected");
                Assert.True(accept.IsEffectivelyVisible);
                Assert.True(accept.IsEffectivelyEnabled);
                var origin = accept.TranslatePoint(new Point(), view)!.Value;
                Assert.True(origin.X >= 0 && origin.X + accept.Bounds.Width <= view.Bounds.Width,
                    $"Bulk toolbar exceeds {view.Bounds.Width}px: {origin.X} + {accept.Bounds.Width}");
                if (Environment.GetEnvironmentVariable("PB_RENDER_OUT") is { Length: > 0 } output)
                {
                    Directory.CreateDirectory(output);
                    using var bitmap = new RenderTargetBitmap(new PixelSize(width, 700));
                    bitmap.Render(window);
                    bitmap.Save(Path.Combine(output, $"story-suggestions-bulk-{theme}-{width}.png"), new PngBitmapEncoderOptions());
                }
            }
            finally { window.Close(); }
        });
    }
}
