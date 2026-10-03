using Avalonia.Input;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Input;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Preferences &gt; Keyboard Shortcuts on the input service (docs/superpowers/specs/2026-10-03-input-service-design.md §9): rows and groups, capture, removing and resetting bindings, conflict
/// flagging, and layout import/export. These replace the registry-and-database tests the editor had before (one binding row per command, a curated key list, "never zero bindings").
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ShortcutsEditorViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"paperbunkr_shortcuts_editor_test_{Guid.NewGuid():N}");

    public ShortcutsEditorViewModelTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class FakePicker : IFilePickerService
    {
        public string? OpenPath { get; set; }

        public string? SavePath { get; set; }

        public Task<string?> PickOpenFileAsync(string title, string extension, string extensionLabel) => Task.FromResult(OpenPath);

        public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string extension, string extensionLabel) => Task.FromResult(SavePath);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);

        public Task SetClipboardTextAsync(string text) => Task.CompletedTask;
    }

    private (ShortcutsEditorViewModel Editor, InputService Input, List<(string Title, string Message)> Toasts) Create(FakePicker? picker = null, MemoryKeymapStore? store = null)
    {
        var input = new InputService(InputActionCatalog.CreateWithCoreActions(), store ?? new MemoryKeymapStore());
        var toasts = new List<(string, string)>();
        var editor = new ShortcutsEditorViewModel(input, picker ?? new FakePicker(), (title, message) => toasts.Add((title, message)));
        return (editor, input, toasts);
    }

    private static ShortcutRowViewModel Row(ShortcutsEditorViewModel editor, string actionId) =>
        editor.Groups.SelectMany(g => g.Rows).Single(r => r.ActionId == actionId);

    private static InputBinding Key_(Key key, KeyModifiers modifiers = KeyModifiers.None) => InputBinding.ForKey(key, modifiers);

    // ----- Rows and groups -----

    [Fact]
    public void OneRowPerRegisteredAction_GroupedByTheCatalog()
    {
        var (editor, input, _) = Create();

        Assert.Equal(input.Actions.All.Count, editor.Groups.Sum(g => g.Rows.Count));
        Assert.Equal(input.Actions.All.Select(i => i.Group).Distinct(), editor.Groups.Select(g => g.Title));
        Assert.Equal(["Left"], Row(editor, InputActionIds.PageTurnLeft).Chips.Select(c => c.Label).Take(1));
        Assert.Contains("Ctrl+Wheel up", Row(editor, InputActionIds.ZoomIn).Chips.Select(c => c.Label));
    }

    [Fact]
    public void GroupTags_MatchThePreferencesSearchAnchors()
    {
        var (editor, _, _) = Create();

        var tags = editor.Groups.ToDictionary(g => g.Title, g => g.Tag);

        Assert.Equal("shortcuts.navigation", tags["Navigation"]);
        Assert.Equal("shortcuts.zoomFit", tags["Zoom & Fit"]);
        Assert.Equal("shortcuts.display", tags["Display"]);
        Assert.Equal("shortcuts.general", tags["General"]);
        Assert.Equal("shortcuts.library", tags["Library"]);
        Assert.Equal("shortcuts.gamepad", tags["Gamepad"]);
        Assert.Equal("shortcuts.bookReader", tags["Book reader"]);
    }

    [Fact]
    public void EveryGroupTag_IsInThePreferencesSearchIndex()
    {
        var (editor, _, _) = Create();
        var indexed = Paperbunkr.App.Models.PreferenceIndex.Entries.Select(e => e.AnchorKey).ToHashSet();

        foreach (var group in editor.Groups)
        {
            Assert.True(indexed.Contains(group.Tag), $"'{group.Tag}' ({group.Title}) has no PreferenceIndex entry, so Preferences search could not jump to it");
        }
    }

    [Fact]
    public void TheControllersAnalogueAxes_CannotBeCaptured_ButButtonsCan()
    {
        var (editor, _, _) = Create();

        Assert.False(Row(editor, InputActionIds.PanHorizontal).CanCapture);
        Assert.True(Row(editor, InputActionIds.ZoomIn).CanCapture);
    }

    // ----- Capture, add, remove, reset -----

    [Fact]
    public void BeginCapture_ShowsTheBox_AndCancelHidesItAgain()
    {
        var (editor, _, _) = Create();
        var row = Row(editor, InputActionIds.RotateClockwise);

        row.BeginCaptureCommand.Execute(null);
        Assert.True(row.IsCapturing);

        row.CancelCaptureCommand.Execute(null);
        TestDispatcher.Drain();
        Assert.False(row.IsCapturing);
    }

    [Fact]
    public void BeginCapture_OnAnAxisRow_DoesNothing()
    {
        var (editor, _, _) = Create();
        var row = Row(editor, InputActionIds.PanHorizontal);

        row.BeginCaptureCommand.Execute(null);

        Assert.False(row.IsCapturing);
    }

    [Fact]
    public void AddingABinding_KeepsTheExistingOnes_PersistsAndShowsAChip()
    {
        var store = new MemoryKeymapStore();
        var (editor, input, _) = Create(store: store);
        var row = Row(editor, InputActionIds.RotateClockwise);

        row.AddBindingCommand.Execute(Key_(Key.J));
        TestDispatcher.Drain();

        Assert.Equal([Key_(Key.R), Key_(Key.J)], input.GetBindings(InputActionIds.RotateClockwise));
        Assert.Equal(["R", "J"], row.Chips.Select(c => c.Label));
        Assert.Equal(["R", "J"], store.Load().Overrides[InputActionIds.RotateClockwise]);
        Assert.True(row.IsCustomised);
    }

    [Fact]
    public void AddingABindingThatIsAlreadyThere_ChangesNothing()
    {
        var (editor, input, _) = Create();
        var row = Row(editor, InputActionIds.RotateClockwise);
        int changes = 0;
        input.BindingsChanged += (_, _) => changes++;

        row.AddBindingCommand.Execute(Key_(Key.R));
        TestDispatcher.Drain();

        Assert.Equal(0, changes);
        Assert.False(row.IsCustomised);
    }

    [Fact]
    public void AddingABinding_ClosesTheCaptureBox()
    {
        var (editor, _, _) = Create();
        var row = Row(editor, InputActionIds.RotateClockwise);
        row.BeginCaptureCommand.Execute(null);

        row.AddBindingCommand.Execute(Key_(Key.J));
        TestDispatcher.Drain();

        Assert.False(row.IsCapturing);
    }

    [Fact]
    public void RemovingAChip_RemovesThatBindingOnly()
    {
        var (editor, input, _) = Create();
        var row = Row(editor, InputActionIds.NextPage);
        var space = row.Chips.Single(c => c.Binding == Key_(Key.Space));

        row.RemoveChipCommand.Execute(space);
        TestDispatcher.Drain();

        Assert.DoesNotContain(Key_(Key.Space), input.GetBindings(InputActionIds.NextPage));
        Assert.Contains(Key_(Key.PageDown), input.GetBindings(InputActionIds.NextPage));
        Assert.DoesNotContain(row.Chips, c => c.Binding == Key_(Key.Space));
    }

    [Fact]
    public void RemovingTheLastBinding_UnbindsTheAction_WhichTheNewModelAllows()
    {
        var (editor, input, _) = Create();
        var row = Row(editor, InputActionIds.RotateClockwise);

        row.RemoveChipCommand.Execute(row.Chips.Single());
        TestDispatcher.Drain();

        Assert.Empty(input.GetBindings(InputActionIds.RotateClockwise));
        Assert.Empty(row.Chips);
        Assert.True(row.HasNoBindings);
        Assert.True(row.IsCustomised);
    }

    [Fact]
    public void ResetRow_PutsJustThatActionBackToItsDefaults()
    {
        var (editor, input, _) = Create();
        var rotate = Row(editor, InputActionIds.RotateClockwise);
        var zoom = Row(editor, InputActionIds.ZoomIn);
        rotate.AddBindingCommand.Execute(Key_(Key.J));
        zoom.AddBindingCommand.Execute(Key_(Key.K));
        TestDispatcher.Drain();

        rotate.ResetRowCommand.Execute(null);
        TestDispatcher.Drain();

        Assert.Equal([Key_(Key.R)], input.GetBindings(InputActionIds.RotateClockwise));
        Assert.False(rotate.IsCustomised);
        Assert.Contains(Key_(Key.K), input.GetBindings(InputActionIds.ZoomIn));
        Assert.True(zoom.IsCustomised);
    }

    [Fact]
    public void AChangeMadeElsewhere_IsPickedUpByTheRows()
    {
        var (editor, input, _) = Create();
        var row = Row(editor, InputActionIds.RotateClockwise);

        input.SetBindings(InputActionIds.RotateClockwise, [Key_(Key.Q)]);
        TestDispatcher.Drain();

        Assert.Equal(["Q"], row.Chips.Select(c => c.Label));
    }

    [Fact]
    public void ResetAll_RevertsEveryRow_AndClearsTheConflictBanner()
    {
        var (editor, input, toasts) = Create();
        Row(editor, InputActionIds.RotateClockwise).AddBindingCommand.Execute(Key_(Key.Z));   // Z is Zoom in's key: a conflict
        TestDispatcher.Drain();
        Assert.True(editor.HasConflictError);

        editor.ResetAllCommand.Execute(null);
        TestDispatcher.Drain();

        Assert.False(editor.HasConflictError);
        Assert.Equal([Key_(Key.R)], input.GetBindings(InputActionIds.RotateClockwise));
        Assert.All(editor.Groups.SelectMany(g => g.Rows), r => Assert.False(r.IsCustomised));
        Assert.Contains(toasts, t => t.Title == "Keyboard shortcuts reset");
    }

    // ----- Conflicts -----

    [Fact]
    public void AFreshLoad_HasNoConflict_DespiteLeftBeingPageTurnPanAndScroll()
    {
        var (editor, _, _) = Create();

        Assert.False(editor.HasConflictError);
        Assert.All(editor.Groups.SelectMany(g => g.Rows), r => Assert.False(r.IsConflicted));
    }

    [Fact]
    public void TwoRowsSharingABinding_BothAreFlagged_AndRemovingItUnflagsBoth()
    {
        var (editor, _, _) = Create();
        var rotate = Row(editor, InputActionIds.RotateClockwise);
        var zoomIn = Row(editor, InputActionIds.ZoomIn);

        rotate.AddBindingCommand.Execute(Key_(Key.Z));
        TestDispatcher.Drain();

        Assert.True(rotate.IsConflicted);
        Assert.True(zoomIn.IsConflicted);
        Assert.Contains("Rotate clockwise", editor.ConflictError);
        Assert.Contains("Zoom in", editor.ConflictError);

        rotate.RemoveChipCommand.Execute(rotate.Chips.Single(c => c.Binding == Key_(Key.Z)));
        TestDispatcher.Drain();

        Assert.False(rotate.IsConflicted);
        Assert.False(zoomIn.IsConflicted);
        Assert.False(editor.HasConflictError);
    }

    [Fact]
    public void ARowConflictingWithTwoOthers_AllThreeAreFlagged()
    {
        var (editor, _, _) = Create();
        var rotate = Row(editor, InputActionIds.RotateClockwise);
        var zoomIn = Row(editor, InputActionIds.ZoomIn);
        var zoomOut = Row(editor, InputActionIds.ZoomOut);
        zoomOut.AddBindingCommand.Execute(Key_(Key.Z));   // Zoom out also gains plain Z, which Zoom in already has
        rotate.AddBindingCommand.Execute(Key_(Key.Z));
        TestDispatcher.Drain();

        Assert.True(rotate.IsConflicted && zoomIn.IsConflicted && zoomOut.IsConflicted);
    }

    [Fact]
    public void ModeSpecificActionsMayShareAKey_ButAnAlwaysActionCollidingWithOneConflicts()
    {
        var (editor, _, _) = Create();

        // Left is already page turn (unzoomed), pan (zoomed) and scroll (continuous): fine, they are never active together.
        Assert.False(editor.HasConflictError);

        // Rotate clockwise is available in every state, so binding it to Left shadows all three.
        Row(editor, InputActionIds.RotateClockwise).AddBindingCommand.Execute(Key_(Key.Left));
        TestDispatcher.Drain();

        Assert.True(editor.HasConflictError);
        Assert.True(Row(editor, InputActionIds.PageTurnLeft).IsConflicted);
        Assert.True(Row(editor, InputActionIds.PanLeft).IsConflicted);
    }

    [Fact]
    public void TheSameKeyInDifferentScopes_IsNotAConflict()
    {
        var (editor, _, _) = Create();

        // Ctrl+G is "Go to page" in the reader and "Show in Explorer" in the Library; Ctrl+C copies a page in the reader and data in the Library.
        Assert.False(editor.HasConflictError);
        Assert.Equal(Row(editor, InputActionIds.GoToPage).Chips.Single().Label, Row(editor, InputActionIds.LibraryReveal).Chips.Single().Label);
    }

    // ----- Layout import / export -----

    [Fact]
    public async Task ExportThenImport_RoundTripsARemappedBinding()
    {
        string path = Path.Combine(_dir, "layout.json");
        var picker = new FakePicker { SavePath = path, OpenPath = path };
        var (editor, input, _) = Create(picker);
        input.SetBindings(InputActionIds.PageTurnLeft, [Key_(Key.J)]);
        TestDispatcher.Drain();

        await editor.ExportLayoutCommand.ExecuteAsync(null);
        input.SetBindings(InputActionIds.PageTurnLeft, [Key_(Key.K)]);
        await editor.ImportLayoutCommand.ExecuteAsync(null);

        Assert.Equal([Key_(Key.J)], input.GetBindings(InputActionIds.PageTurnLeft));
        Assert.Equal(["J"], Row(editor, InputActionIds.PageTurnLeft).Chips.Select(c => c.Label));
    }

    [Fact]
    public async Task Export_WritesTheCompleteEffectiveLayout_IncludingMouseWheelAndGamepad()
    {
        string path = Path.Combine(_dir, "layout.json");
        var (editor, _, _) = Create(new FakePicker { SavePath = path });

        await editor.ExportLayoutCommand.ExecuteAsync(null);

        string json = File.ReadAllText(path);
        Assert.Contains("Ctrl+WheelUp", json);
        Assert.Contains("Mouse4", json);
        Assert.Contains("Pad:A", json);
        Assert.Contains(InputActionIds.ZoomIn, json);
    }

    [Fact]
    public async Task Import_WithOneCorruptEntry_StillAppliesTheValidOnes()
    {
        string path = Path.Combine(_dir, "layout.json");
        File.WriteAllText(path, $$"""
            [
                {"CommandId": "{{InputActionIds.PageTurnLeft}}", "Gesture": "J"},
                {"CommandId": "{{InputActionIds.PageTurnRight}}", "Gesture": "not a real gesture"}
            ]
            """);
        var (editor, input, _) = Create(new FakePicker { OpenPath = path });

        await editor.ImportLayoutCommand.ExecuteAsync(null);

        Assert.Equal([Key_(Key.J)], input.GetBindings(InputActionIds.PageTurnLeft));
        Assert.Equal([Key_(Key.Right)], input.GetBindings(InputActionIds.PageTurnRight).Take(1));
    }

    [Fact]
    public async Task Import_MultipleEntriesForOneCommand_AppliesThemAsSeparateBindings()
    {
        string path = Path.Combine(_dir, "layout.json");
        File.WriteAllText(path, $$"""
            [
                {"CommandId": "{{InputActionIds.PageTurnLeft}}", "Gesture": "J"},
                {"CommandId": "{{InputActionIds.PageTurnLeft}}", "Gesture": "Ctrl+WheelDown"},
                {"CommandId": "{{InputActionIds.PageTurnLeft}}", "Gesture": "Mouse4"}
            ]
            """);
        var (editor, input, toasts) = Create(new FakePicker { OpenPath = path });

        await editor.ImportLayoutCommand.ExecuteAsync(null);

        Assert.Equal(
            [Key_(Key.J), InputBinding.ForWheel(WheelDirection.Down, KeyModifiers.Control), InputBinding.ForMouseButton(MouseButton.XButton1)],
            input.GetBindings(InputActionIds.PageTurnLeft));
        Assert.Contains(toasts, t => t.Title == "Keyboard shortcuts imported" && t.Message.Contains("3 bindings"));
    }

    [Fact]
    public async Task Import_ALayoutFromThePreviousSystem_StillWorks()
    {
        string path = Path.Combine(_dir, "layout.json");
        // The old editor exported KeyGesture strings, e.g. "Ctrl+Shift+C".
        File.WriteAllText(path, $$"""[ {"CommandId": "{{InputActionIds.ClipRegion}}", "Gesture": "Ctrl+Shift+J"} ]""");
        var (editor, input, _) = Create(new FakePicker { OpenPath = path });

        await editor.ImportLayoutCommand.ExecuteAsync(null);

        Assert.Equal([Key_(Key.J, KeyModifiers.Control | KeyModifiers.Shift)], input.GetBindings(InputActionIds.ClipRegion));
    }

    [Fact]
    public async Task Import_OfSomethingThatIsNotALayout_ToastsAndChangesNothing()
    {
        string path = Path.Combine(_dir, "layout.json");
        File.WriteAllText(path, "this is not json");
        var (editor, input, toasts) = Create(new FakePicker { OpenPath = path });
        var before = input.GetBindings(InputActionIds.PageTurnLeft);

        await editor.ImportLayoutCommand.ExecuteAsync(null);

        Assert.Equal(before, input.GetBindings(InputActionIds.PageTurnLeft));
        Assert.Contains(toasts, t => t.Title == "Couldn't import keyboard shortcuts");
    }

    [Fact]
    public async Task ImportAndExport_WhenThePickerIsCancelled_DoNothing()
    {
        var (editor, _, toasts) = Create(new FakePicker());

        await editor.ImportLayoutCommand.ExecuteAsync(null);
        await editor.ExportLayoutCommand.ExecuteAsync(null);

        Assert.Empty(toasts);
    }

    // ----- Actions registered after the screen was built -----

    [Fact]
    public void ARegisteredPluginAction_GetsARowOnRefresh()
    {
        var (editor, input, _) = Create();
        input.Actions.Register(new InputActionInfo("Plugin.Demo.Hello", "Demo plugin", "Say hello", InputScope.Global, InputContext.Always, [Key_(Key.F9, KeyModifiers.Control)]));

        editor.Refresh();

        Assert.Equal("Demo plugin", editor.Groups.Last().Title);
        Assert.Equal(["Ctrl+F9"], Row(editor, "Plugin.Demo.Hello").Chips.Select(c => c.Label));
    }
}
