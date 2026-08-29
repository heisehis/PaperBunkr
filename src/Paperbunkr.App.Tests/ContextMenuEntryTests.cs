using System.Linq;
using Paperbunkr.App.ContextMenus;

namespace Paperbunkr.App.Tests;

public class ContextMenuEntryTests
{
    [Fact]
    public void Separator_IsShared_AndFlagged()
    {
        Assert.True(ContextMenuEntry.Separator.IsSeparator);
        Assert.Same(ContextMenuEntry.Separator, ContextMenuEntry.Separator);
    }

    [Fact]
    public void Item_CarriesEveryField()
    {
        var command = new RelayStub();
        var entry = ContextMenuEntry.Item("Delete", command, parameter: 7, isEnabled: false, isDanger: true, inputGesture: "Ctrl+I");

        Assert.Equal("Delete", entry.Header);
        Assert.Same(command, entry.Command);
        Assert.Equal(7, entry.CommandParameter);
        Assert.False(entry.IsEnabled);
        Assert.True(entry.IsDanger);
        Assert.Equal("Ctrl+I", entry.InputGesture);
        Assert.False(entry.IsSeparator);
    }

    [Fact]
    public void SubMenu_DropsNullChildren()
    {
        var sub = ContextMenuEntry.SubMenu("Parent", new ContextMenuEntry?[]
        {
            ContextMenuEntry.Item("A", null),
            null,
            ContextMenuEntry.Item("B", null),
        });

        Assert.NotNull(sub);
        Assert.Equal(new[] { "A", "B" }, sub!.Children!.Select(c => c.Header));
    }

    [Fact]
    public void SubMenu_ReturnsNull_WhenHiddenOrEmpty()
    {
        Assert.Null(ContextMenuEntry.SubMenu("Parent", new[] { ContextMenuEntry.Item("A", null) }, isVisible: false));
        Assert.Null(ContextMenuEntry.SubMenu("Parent", new ContextMenuEntry?[] { null, null }));
    }

    private sealed class RelayStub : System.Windows.Input.ICommand
    {
        public event System.EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) { }
    }
}
