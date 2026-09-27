using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Paperbunkr.App.Controls;

namespace Paperbunkr.App.Tests;

/// <summary>
/// docs/superpowers/specs/2026-09-26-library-health-subtabs-design.md - <see cref="VirtualList"/> builds only the rows in view,
/// sizes to its content when short, and builds nothing while hidden. Headless with the real Fluent theme applied (without a
/// theme, buttons and item controls have no templates and every count would be meaningless).
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class VirtualListTests
{
    private const string RowTag = "vl-row";

    private static readonly FuncDataTemplate<int> RowTemplate = new((i, _) => new Border
    {
        Tag = RowTag,
        Height = 60,
        Child = new StackPanel { Children = { new TextBlock { Text = $"Row {i}" }, new Button { Content = "Resolve" } } },
    }, true);

    private static int RealizedRows(Visual root) => root.GetVisualDescendants().OfType<Border>().Count(b => (b.Tag as string) == RowTag);

    /// <summary>Runs <paramref name="check"/> against a shown window hosting the list, with the Fluent theme applied for the duration.</summary>
    private static void WithList(int itemCount, Action<VirtualList, Window> check, bool visible = true)
    {
        var theme = new Avalonia.Themes.Fluent.FluentTheme();
        Application.Current!.Styles.Add(theme);
        try
        {
            var list = new VirtualList { ItemTemplate = RowTemplate, ItemsSource = Enumerable.Range(0, itemCount).ToList(), IsVisible = visible };
            var page = new ScrollViewer { Content = new StackPanel { Children = { new TextBlock { Text = "page" }, list } } };
            var window = new Window { Width = 900, Height = 800, Content = page };
            window.Show();
            try
            {
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                check(list, window);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            Application.Current!.Styles.Remove(theme);
        }
    }

    [Fact]
    public void ALongList_BuildsOnlyTheRowsInView()
    {
        WithList(1000, (list, window) =>
        {
            int realized = RealizedRows(window);
            Assert.InRange(realized, 1, 25); // 1,000 rows, a 420 px region of 60 px rows: about 7 plus the panel's small buffer
            Assert.True(list.Bounds.Height <= VirtualList.DefaultMaxListHeight + 1, $"the list grew to {list.Bounds.Height}");
        });
    }

    [Fact]
    public void AShortList_IsExactlyAsTallAsItsRows_NotTheMaximum()
    {
        WithList(3, (list, window) =>
        {
            Assert.Equal(3, RealizedRows(window));
            Assert.InRange(list.Bounds.Height, 170, 200); // 3 x 60 px, not 420
        });
    }

    [Fact]
    public void AHiddenList_BuildsNothing()
    {
        WithList(1000, (_, window) => Assert.Equal(0, RealizedRows(window)), visible: false);
    }

    [Fact]
    public void MaxListHeight_CapsTheRegion()
    {
        WithList(1000, (list, window) =>
        {
            list.MaxListHeight = 200;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.True(list.Bounds.Height <= 201, $"the list is {list.Bounds.Height} tall");
            Assert.InRange(RealizedRows(window), 1, 15);
        });
    }
}
