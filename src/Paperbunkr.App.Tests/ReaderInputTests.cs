using Avalonia.Input;
using Paperbunkr.App.Controls;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services.Input;
using Paperbunkr.App.Views;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Reading-order page commands, their extra default bindings and reader-state contexts, and the tap zone overlay geometry (docs/superpowers/specs/2026-09-25-comic-reader-reach-design.md,
/// docs/superpowers/specs/2026-10-03-input-service-design.md). Originally written against the keyboard command registry and its database table; the bindings now live in the input service.
/// </summary>
public class ReaderInputTests
{
    private static InputService CreateService() => new(InputActionCatalog.CreateWithCoreActions(), new MemoryKeymapStore());

    [Fact]
    public void NextAndPreviousPage_ShipClickerMediaAndSpaceDefaults()
    {
        var service = CreateService();

        Assert.Equal(
            [InputBinding.ForKey(Key.PageDown), InputBinding.ForKey(Key.Space), InputBinding.ForKey(Key.MediaNextTrack),
             InputBinding.ForPad(GamepadInput.A), InputBinding.ForPad(GamepadInput.RightShoulder), InputBinding.ForMouseButton(MouseButton.XButton2)],
            service.GetBindings(InputActionIds.NextPage));
        Assert.Equal(
            [InputBinding.ForKey(Key.PageUp), InputBinding.ForKey(Key.Space, KeyModifiers.Shift), InputBinding.ForKey(Key.MediaPreviousTrack),
             InputBinding.ForPad(GamepadInput.B), InputBinding.ForPad(GamepadInput.LeftShoulder), InputBinding.ForMouseButton(MouseButton.XButton1)],
            service.GetBindings(InputActionIds.PreviousPage));
    }

    [Fact]
    public void ACustomBinding_ReplacesTheAdditionalDefaults()
    {
        var service = CreateService();

        service.SetBindings(InputActionIds.NextPage, [InputBinding.ForKey(Key.D)]);

        Assert.Equal([InputBinding.ForKey(Key.D)], service.GetBindings(InputActionIds.NextPage));
    }

    [Fact]
    public void ResetAll_RestoresTheAdditionalDefaults()
    {
        var service = CreateService();
        service.SetBindings(InputActionIds.NextPage, [InputBinding.ForKey(Key.D)]);

        service.ResetAll();

        Assert.Equal(6, service.GetBindings(InputActionIds.NextPage).Count);
    }

    [Fact]
    public void ReadingOrderCommands_AreBoundToPagedModeOnly()
    {
        var catalog = InputActionCatalog.CreateWithCoreActions();

        Assert.Equal(InputContext.Paged, catalog.Find(InputActionIds.NextPage)!.Context);
        Assert.Equal(InputContext.Paged, catalog.Find(InputActionIds.PreviousPage)!.Context);
    }

    [Theory]
    [InlineData(InputContext.Always, InputContext.Continuous, true)]
    [InlineData(InputContext.Paged, InputContext.PagedUnzoomed, true)]
    [InlineData(InputContext.PagedZoomed, InputContext.Paged, true)]
    [InlineData(InputContext.Paged, InputContext.Paged, true)]
    [InlineData(InputContext.Paged, InputContext.Continuous, false)]
    [InlineData(InputContext.PagedUnzoomed, InputContext.PagedZoomed, false)]
    [InlineData(InputContext.Continuous, InputContext.Continuous, true)]
    public void Overlaps_MatchesRuntimeExclusivity(InputContext a, InputContext b, bool expected) =>
        Assert.Equal(expected, a.Overlaps(b));

    [Fact]
    public void NoTwoActionsThatCanBeActiveTogetherShareADefaultBinding()
    {
        var catalog = InputActionCatalog.CreateWithCoreActions();
        var keymap = new InputKeymap(catalog, new KeymapConfig());

        foreach (var info in catalog.All)
        {
            foreach (var binding in info.Defaults)
            {
                Assert.Empty(keymap.FindConflicts(info.Action, binding));
            }
        }
    }

    // --- Overlay geometry (sampled from the resolver, so it shows exactly what a tap does) ---

    [Fact]
    public void Overlay_DisabledLayout_HasNoRegions() =>
        Assert.Empty(TapZoneOverlay.BuildRegions(TapZoneLayout.Disabled, TapZoneInvert.None, false, false));

    [Fact]
    public void Overlay_ContinuousDefault_HasNoRegions() =>
        Assert.Empty(TapZoneOverlay.BuildRegions(TapZoneLayout.Default, TapZoneInvert.None, false, true));

    [Fact]
    public void Overlay_DefaultPaged_IsThreeColumns()
    {
        var regions = TapZoneOverlay.BuildRegions(TapZoneLayout.Default, TapZoneInvert.None, false, false);

        Assert.Equal([TapAction.Left, TapAction.Menu, TapAction.Right], regions.Select(r => r.Action));
        Assert.All(regions, r => Assert.Equal(1.0, r.Rect.Height, 6));
    }

    [Fact]
    public void Overlay_LShaped_CoversTheWholeFrameWithoutOverlap()
    {
        var regions = TapZoneOverlay.BuildRegions(TapZoneLayout.LShaped, TapZoneInvert.None, false, false);

        Assert.Equal(1.0, regions.Sum(r => r.Rect.Width * r.Rect.Height), 6);
        Assert.Contains(regions, r => r.Action == TapAction.Menu);
        Assert.Contains(regions, r => r.Action == TapAction.Previous);
        Assert.Contains(regions, r => r.Action == TapAction.Next);
    }

    [Fact]
    public void Overlay_RightToLeft_SwapsPreviousAndNextSides()
    {
        var ltr = TapZoneOverlay.BuildRegions(TapZoneLayout.Kindlish, TapZoneInvert.None, false, false);
        var rtl = TapZoneOverlay.BuildRegions(TapZoneLayout.Kindlish, TapZoneInvert.None, true, false);

        Assert.Equal(TapAction.Previous, ltr.First(r => r.Rect.X < 0.1 && r.Rect.Y > 0.3).Action);
        Assert.Equal(TapAction.Previous, rtl.First(r => r.Rect.Right > 0.9 && r.Rect.Y > 0.3).Action);
    }

    [Theory]
    [InlineData(TapAction.Previous, "Previous")]
    [InlineData(TapAction.Next, "Next")]
    [InlineData(TapAction.Menu, "Menu")]
    [InlineData(TapAction.Left, "Left")]
    [InlineData(TapAction.Right, "Right")]
    [InlineData(TapAction.None, "")]
    public void Overlay_LabelFor(TapAction action, string expected) => Assert.Equal(expected, TapZoneOverlay.LabelFor(action));
}
