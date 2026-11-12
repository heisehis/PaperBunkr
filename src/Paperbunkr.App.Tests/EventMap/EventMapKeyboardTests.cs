using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Paperbunkr.App.Controls.EventMap;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.EventMap;
using Paperbunkr.App.Services.Input;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests.EventMap;

/// <summary>The Event Map's arrow keys driven through a real view in a real window: focus has to stay on the selected card as the map scrolls, and an arrow with nowhere to go on the map has to reach the shell.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class EventMapKeyboardTests
{
    [Fact]
    public void MatrixKeepsSelectionAndEscapeClosesExpansionBeforeInspector()
    {
        Build(issues: 30, series: 3, rig =>
        {
            rig.Vm.Select(0);
            rig.Vm.ShowMatrixModeCommand.Execute(null);
            RunLayout(rig.Window);
            var matrix = rig.View.FindControl<EventMatrixCanvas>("MatrixCanvas")!;
            matrix.Focus();
            Press(rig.Window, Key.Enter);
            Assert.True(matrix.HasExpansion);
            rig.Vm.OpenInspectorCommand.Execute(null);
            Press(rig.Window, Key.Escape);
            Assert.False(matrix.HasExpansion);
            Assert.True(rig.Vm.IsInspectorOpen);
            Assert.Equal(0, rig.Vm.SelectedIndex);
        });
    }

    [Fact]
    public void RollCallHidesAndRestoresSeriesInBothViews()
    {
        Build(issues: 30, series: 3, rig =>
        {
            var row = rig.Vm.RollCallRows[0];
            row.IsShown = false;
            TestDispatcher.Drain();
            Assert.DoesNotContain(rig.Vm.Layout.Cells, c => c.Row.SeriesId == row.SeriesId);
            Assert.DoesNotContain(rig.Vm.Matrix.Rows, r => r.SeriesId == row.SeriesId);
            rig.Vm.ShowAllSeriesCommand.Execute(null);
            Assert.Contains(rig.Vm.Matrix.Rows, r => r.SeriesId == row.SeriesId);
        });
    }
    private sealed record Rig(Window Window, EventMapView View, EventMapViewModel Vm, EventMapCanvas Canvas, IInputService Input, List<Key> Unhandled);

    private static Rig Build(int issues, int series, Action<Rig> body)
    {
        Rig? rig = null;
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            var lanes = Enumerable.Range(1, series).Select(i => new ContinuityLane(i, $"Series {i}", false)).ToArray();
            var loose = Enumerable.Range(1, issues).Select(i => new EventMapRow(i, i, 0, Paperbunkr.Data.Entities.EventMembershipRole.Core,
                ((i - 1) % series) + 1, $"Series {((i - 1) % series) + 1}", ((i - 1) / series + 1).ToString(), 2000 + i, 1, false, EventMapReadState.Unread)).ToArray();
            var data = new ContinuityMapData(7, "Big", lanes, Array.Empty<ContinuityEventData>(), Array.Empty<ContinuityRelationData>(), loose,
                new Dictionary<int, IReadOnlyList<EventMapEventRef>>(), PendingDuplicatePairs: 0);
            var vm = new EventMapViewModel(
                activity: new ActivityService(dispatch: a => a(), recordRun: _ => { }),
                runInBackground: work => Task.FromResult(work()),
                loadContinuity: _ => data);
            vm.LoadContinuityAsync(7).GetAwaiter().GetResult();

            var view = new EventMapView { DataContext = vm };
            var input = ReaderTestInput.Create();
            view.InputService = input;
            var window = new Window { Content = view, Width = 900, Height = 420 };
            InputHost.Attach(window, input);
            var unhandled = new List<Key>();
            // What the shell's own arrow fallback would see: bubbling, so only keys the map did not use.
            window.AddHandler(InputElement.KeyDownEvent, (_, e) => unhandled.Add(e.Key), RoutingStrategies.Bubble);
            window.Show();
            RunLayout(window);
            RunLayout(window);
            rig = new Rig(window, view, vm, view.FindControl<EventMapCanvas>("MapCanvas")!, input, unhandled);
            body(rig);
            window.Close();
        });
        return rig!;
    }

    private static string Where(Rig rig)
    {
        var focused = rig.Window.FocusManager!.GetFocusedElement();
        string card = focused is Control other ? other.GetType().Name : "nothing";
        return $"selected {rig.Vm.SelectedIndex}, focus on {card}, offset {rig.Canvas.Offset}";
    }

    private static bool FocusOnSelected(Rig rig) =>
        ReferenceEquals(rig.Window.FocusManager!.GetFocusedElement(), rig.Canvas);

    [Fact]
    public void RemappedZoomActionIsHandledThroughTheInputService()
    {
        Build(issues: 60, series: 8, rig =>
        {
            rig.Input.SetBindings(InputActionIds.ContinuityMapZoomIn, new[] { InputBinding.ForKey(Key.F8) });
            rig.Canvas.Focus(NavigationMethod.Directional);
            double before = rig.Vm.Scale;
            Press(rig.Window, Key.F8);
            Assert.True(rig.Vm.Scale > before);
        });
    }

    [Fact]
    public void RightArrow_KeepsFocusOnTheSelectedCard_AsTheMapScrolls()
    {
        Build(issues: 60, series: 8, rig =>
        {
            rig.Vm.Select(0, reveal: false);
            RunLayout(rig.Window);
            rig.Canvas.Focus(NavigationMethod.Directional);
            RunLayout(rig.Window);
            Assert.True(FocusOnSelected(rig), "start: " + Where(rig));

            for (int i = 1; i <= 10; i++)
            {
                int? before = rig.Vm.SelectedIndex;
                Press(rig.Window, Key.Right);
                TestDispatcher.Drain();
                RunLayout(rig.Window);
                Assert.True(rig.Vm.SelectedIndex != before, $"press {i} moved: " + Where(rig));
                Assert.True(FocusOnSelected(rig), $"press {i}: " + Where(rig));
            }
        });
    }

    [Fact]
    public void DownAndUp_KeepFocusOnTheSelectedCard()
    {
        Build(issues: 60, series: 8, rig =>
        {
            rig.Vm.Select(0, reveal: false);
            RunLayout(rig.Window);
            rig.Canvas.Focus(NavigationMethod.Directional);
            RunLayout(rig.Window);

            for (int i = 0; i < 6; i++)
            {
                Press(rig.Window, Key.Down);
                TestDispatcher.Drain();
                RunLayout(rig.Window);
                Assert.True(FocusOnSelected(rig), $"down {i}: " + Where(rig));
            }

            for (int i = 0; i < 6; i++)
            {
                Press(rig.Window, Key.Up);
                TestDispatcher.Drain();
                RunLayout(rig.Window);
                Assert.True(FocusOnSelected(rig), $"up {i}: " + Where(rig));
            }
        });
    }

    [Theory]
    [InlineData(Key.Left)]
    [InlineData(Key.Up)]
    public void AnArrowWithNowhereToGoOnTheMap_ReachesTheShell(Key key)
    {
        Build(issues: 24, series: 6, rig =>
        {
            rig.Vm.Select(0, reveal: false);
            RunLayout(rig.Window);
            rig.Canvas.Focus(NavigationMethod.Directional);
            RunLayout(rig.Window);
            rig.Unhandled.Clear();

            Press(rig.Window, key);

            Assert.Contains(key, rig.Unhandled);
            Assert.Equal(0, rig.Vm.SelectedIndex);
        });
    }

    [Fact]
    public void MapCanvasIsTheSingleFocusableMapElement()
    {
        Build(issues: 24, series: 6, rig =>
        {
            rig.Vm.Select(0, reveal: false);
            rig.Canvas.Focus(NavigationMethod.Directional);
            Assert.True(rig.Canvas.IsFocused);
            Assert.Null(rig.Canvas.FocusAdorner);
        });
    }
}
