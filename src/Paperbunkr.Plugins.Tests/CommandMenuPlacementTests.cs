using Paperbunkr.Plugins.Hooks;

namespace Paperbunkr.Plugins.Tests;

/// <summary>
/// Where a command shows (Plugin API 4.5, docs/superpowers/specs/2026-10-28-plugin-api-4-5-pitch-completion-design.md §E): with no
/// <c>menus</c> attribute nothing changes from before 4.5; with one, exactly the listed menus. The manifest's <c>menus</c>/<c>toolbar</c>
/// reach the command through the real manifest reader.
/// </summary>
public sealed class CommandMenuPlacementTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pb-menus-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static CSharpCommand Command(string hook, string? menus = null) => new()
    {
        PluginKey = "p",
        Hook = hook,
        Key = "p.k",
        Name = "k",
        Menus = menus,
        ScriptPath = "x.csx",
    };

    [Theory]
    [InlineData("library", true)]
    [InlineData("detail", true)]
    [InlineData("readinglist", false)]
    [InlineData("books", false)]
    public void A_library_command_without_menus_keeps_its_old_places(string menu, bool shown) =>
        Assert.Equal(shown, Command(PluginHooks.Library).ShowsInMenu(menu));

    [Fact]
    public void A_books_command_without_menus_shows_only_in_books_and_other_hooks_nowhere()
    {
        Assert.True(Command(PluginHooks.Books).ShowsInMenu("books"));
        Assert.False(Command(PluginHooks.Books).ShowsInMenu("library"));
        Assert.False(Command(PluginHooks.Startup).ShowsInMenu("library"));
    }

    [Fact]
    public void Declared_menus_are_exactly_the_listed_ones_in_any_case_and_spacing()
    {
        var command = Command(PluginHooks.Library, " ReadingList , detail ");

        Assert.True(command.ShowsInMenu("readinglist"));
        Assert.True(command.ShowsInMenu("detail"));
        Assert.False(command.ShowsInMenu("library"));
    }

    [Fact]
    public void Menus_and_toolbar_come_from_the_manifest()
    {
        Directory.CreateDirectory(_root);
        string manifest = Path.Combine(_root, "plugin.xml");
        File.WriteAllText(manifest, """
            <Plugin key="p" name="P">
              <Command hook="Library" key="p.a" name="A" script="a.csx" menus="readinglist" toolbar="true" />
              <Command hook="Library" key="p.b" name="B" script="b.py" method="run" />
            </Plugin>
            """);

        var commands = XmlPluginInitializer.GetCommands(manifest).ToList();

        Assert.Equal(("readinglist", true), (commands[0].Menus, commands[0].Toolbar));
        Assert.Equal(((string?)null, false), (commands[1].Menus, commands[1].Toolbar));
    }
}
